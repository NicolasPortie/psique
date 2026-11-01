using System.Text.Json;
using System.Text.RegularExpressions;
using Psique.Api.Data;
using Psique.Api.DTOs.WhatsApp;
using Psique.Api.Models;
using Psique.Api.Services.Notificacoes;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Psique.Api.Shared.Texto;
using Microsoft.EntityFrameworkCore;

namespace Psique.Api.Services.WhatsApp;

public sealed partial class BridgeWhatsAppService(
    AppDbContext dbContext,
    IProtecaoDadosService protecao,
    IAuditoriaService auditoria,
    INotificacoesService notificacoes,
    DetectorConteudoSensiveisService detectorSensiveis) : IBridgeWhatsAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task<ReceberEventoBridgeResponse> ReceberEventoAsync(
        string tokenBridge,
        ReceberEventoBridgeRequest request,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenBridge))
            throw new RegraNegocioException("Token do Bridge ausente.", StatusCodes.Status401Unauthorized);

        var tokenHash = protecao.HashToken(tokenBridge.Trim());
        var integracao = await dbContext.IntegracoesWhatsApp
            .Include(item => item.ContaPsicologo)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash && item.Ativo, cancellationToken)
            ?? throw new RegraNegocioException("Integração Bridge não autorizada.", StatusCodes.Status401Unauthorized);

        if (integracao.Status is StatusIntegracao.Inativa)
            throw new RegraNegocioException("Integração Bridge inativa.", StatusCodes.Status403Forbidden);
        if (integracao.ContaPsicologo.Status != StatusContaPsicologo.Ativa)
            throw new RegraNegocioException("A conta não está ativa para receber mensagens.", StatusCodes.Status403Forbidden);

        var telefone = NormalizarTelefone(request.Telefone);
        await using var travaConfirmacao = !string.IsNullOrWhiteSpace(request.MensagemExternaId)
            ? await Shared.Concorrencia.TravaOperacao.AdquirirAsync(dbContext,
                $"confirmacao-whatsapp:{integracao.ContaPsicologoId:N}:{request.MensagemExternaId}", cancellationToken) : null;
        var payloadMinimo = new
        {
            request.TipoEvento,
            Telefone = telefone,
            request.NomeContato,
            request.MensagemExternaId,
            PossuiConteudo = !string.IsNullOrWhiteSpace(request.Conteudo)
        };
        var payloadJson = protecao.Proteger(JsonSerializer.Serialize(payloadMinimo, JsonOptions));
        var payloadHash = protecao.HashDeterministico($"{integracao.Id}:{request.EventoExternoId}:{request.TipoEvento}:{telefone}:{request.MensagemExternaId}");

        if (!string.IsNullOrWhiteSpace(request.EventoExternoId))
        {
            var duplicadoEvento = await dbContext.EventosBridgeWhatsApp.AsNoTracking().AnyAsync(
                item => item.ContaPsicologoId == integracao.ContaPsicologoId &&
                        item.IntegracaoWhatsAppId == integracao.Id &&
                        item.EventoExternoId == request.EventoExternoId,
                cancellationToken);
            if (duplicadoEvento)
            {
                return new ReceberEventoBridgeResponse(Guid.Empty, true, true, null, "Evento já processado (idempotência).");
            }
        }

        var evento = new EventoBridgeWhatsApp
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = integracao.ContaPsicologoId,
            IntegracaoWhatsAppId = integracao.Id,
            EventoExternoId = string.IsNullOrWhiteSpace(request.EventoExternoId) ? null : request.EventoExternoId.Trim(),
            TipoEvento = request.TipoEvento.Trim(),
            MensagemExternaId = request.MensagemExternaId,
            StatusMensagem = request.TipoEvento.Trim().ToLowerInvariant() switch
            {
                "mensagem.entregue" or "message.delivered" => StatusMensagemWhatsApp.Entregue,
                "mensagem.lida" or "message.read" => StatusMensagemWhatsApp.Lida,
                "mensagem.falhou" or "message.failed" => StatusMensagemWhatsApp.Falhou,
                _ => null
            },
            PayloadHash = payloadHash,
            PayloadJson = payloadJson,
            Processado = false,
            CriadoEm = DateTimeOffset.UtcNow
        };
        dbContext.EventosBridgeWhatsApp.Add(evento);

        try
        {
            var tipo = request.TipoEvento.Trim().ToLowerInvariant();
            Guid? conversaId = null;

            if (tipo is "mensagem.recebida" or "message.received" or "mensagem_recebida")
            {
                conversaId = await ProcessarMensagemRecebidaAsync(integracao, telefone, request, cancellationToken);
            }
            else if (tipo is "mensagem.entregue" or "message.delivered")
            {
                await AtualizarStatusMensagemAsync(integracao.ContaPsicologoId, request.MensagemExternaId, StatusMensagemWhatsApp.Entregue, cancellationToken);
            }
            else if (tipo is "mensagem.lida" or "message.read")
            {
                await AtualizarStatusMensagemAsync(integracao.ContaPsicologoId, request.MensagemExternaId, StatusMensagemWhatsApp.Lida, cancellationToken);
            }
            else if (tipo is "mensagem.falhou" or "message.failed")
            {
                await AtualizarStatusMensagemAsync(integracao.ContaPsicologoId, request.MensagemExternaId, StatusMensagemWhatsApp.Falhou, cancellationToken);
            }

            evento.Processado = true;
            evento.ProcessadoEm = DateTimeOffset.UtcNow;
            // A chegada de uma mensagem/ACK não confirma a conexão atual da Evolution.
            // O contrato genérico legado conserva seu comportamento de sinal de atividade.
            if (!EvolutionApiClient.Compativel(integracao))
            {
                integracao.Status = StatusIntegracao.Ativa;
                integracao.UltimoErro = null;
            }
            integracao.AtualizadoEm = DateTimeOffset.UtcNow;

            auditoria.Registrar(
                integracao.ContaPsicologoId,
                null,
                "EventoBridgeProcessado",
                nameof(EventoBridgeWhatsApp),
                evento.Id,
                "Evento do Bridge processado.",
                null,
                new { evento.TipoEvento, conversaId },
                ip,
                userAgent);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new ReceberEventoBridgeResponse(evento.Id, true, false, conversaId, "Evento processado.");
        }
        catch (Exception ex) when (ex is not RegraNegocioException)
        {
            evento.Processado = false;
            evento.ErroProcessamento = "Falha ao processar evento do Bridge.";
            integracao.Status = StatusIntegracao.Erro;
            integracao.UltimoErro = evento.ErroProcessamento;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new RegraNegocioException("Falha ao processar evento do Bridge.", StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<Guid> ProcessarMensagemRecebidaAsync(
        IntegracaoWhatsApp integracao,
        string telefone,
        ReceberEventoBridgeRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.MensagemExternaId))
        {
            var mensagemExistente = await dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
                item => item.ContaPsicologoId == integracao.ContaPsicologoId &&
                        item.MensagemExternaId == request.MensagemExternaId,
                cancellationToken);
            if (mensagemExistente)
                return Guid.Empty;
        }

        var telefoneHash = Data.Cifra.CifraDeColuna.HashTelefone(telefone);
        var conversa = await dbContext.ConversasWhatsApp
            .FirstOrDefaultAsync(
                item => item.ContaPsicologoId == integracao.ContaPsicologoId &&
                        item.IntegracaoWhatsAppId == integracao.Id &&
                        item.TelefoneHash == telefoneHash &&
                        item.Status != StatusConversaWhatsApp.Finalizada &&
                        item.Status != StatusConversaWhatsApp.Bloqueada,
                cancellationToken);

        if (conversa is null)
        {
            var paciente = await TentarIdentificarPacienteAsync(integracao.ContaPsicologoId, telefone, cancellationToken);
            conversa = new ConversaWhatsApp
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = integracao.ContaPsicologoId,
                IntegracaoWhatsAppId = integracao.Id,
                PacienteId = paciente?.Id,
                Telefone = telefone,
                NomeContato = string.IsNullOrWhiteSpace(request.NomeContato) ? null : request.NomeContato.Trim(),
                Status = StatusConversaWhatsApp.Aberta,
                CriadoEm = DateTimeOffset.UtcNow
            };
            dbContext.ConversasWhatsApp.Add(conversa);
        }

        var conteudoBruto = request.Conteudo?.Trim();
        var sensivel = detectorSensiveis.RequerAtendimentoHumano(conteudoBruto, out var motivoSensivel);
        if (sensivel)
        {
            conversa.AtendimentoHumanoNecessario = true;
            conversa.MotivoAtendimentoHumano = motivoSensivel;
            conversa.Status = StatusConversaWhatsApp.AtendimentoHumano;
        }

        var mensagem = new MensagemWhatsApp
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = integracao.ContaPsicologoId,
            ConversaWhatsAppId = conversa.Id,
            MensagemExternaId = string.IsNullOrWhiteSpace(request.MensagemExternaId) ? null : request.MensagemExternaId.Trim(),
            Direcao = DirecaoMensagem.Entrada,
            TipoMensagem = TipoMensagemWhatsApp.Texto,
            Conteudo = string.IsNullOrWhiteSpace(conteudoBruto) ? null : protecao.Proteger(conteudoBruto),
            Status = StatusMensagemWhatsApp.Recebida,
            RecebidaEm = DateTimeOffset.UtcNow,
            CriadoEm = DateTimeOffset.UtcNow
        };
        dbContext.MensagensWhatsApp.Add(mensagem);

        conversa.UltimaMensagemEm = DateTimeOffset.UtcNow;
        conversa.AtualizadoEm = DateTimeOffset.UtcNow;

        if (sensivel)
        {
            RegistrarRespostaBot(integracao.ContaPsicologoId, conversa, RespostaConteudoSensivel);
            await NotificarEquipeAsync(
                integracao.ContaPsicologoId,
                "Paciente precisa de atenção",
                "Uma mensagem pode ser sensível. Abra a conversa e responda assim que possível.",
                cancellationToken);
            RegistrarEncaminhamentoHumano(integracao.ContaPsicologoId, conversa, motivoSensivel ?? "A mensagem pode tratar de um assunto delicado.");
        }

        // Sessão bot administrativa (executa regras de agenda/consulta, sem fluxo clínico).
        if (!sensivel && conversa.Status == StatusConversaWhatsApp.Aberta)
        {
            var intencao = DetectarIntencaoAdministrativa(conteudoBruto);
            if (intencao == IntencaoFalarComHumano)
            {
                await EncaminharParaHumanoAsync(integracao.ContaPsicologoId, conversa, MotivoPedidoDoPaciente, RespostaPedidoHumano, cancellationToken);
                return conversa.Id;
            }

            var sessao = await dbContext.SessoesBot.FirstOrDefaultAsync(
                item => item.ContaPsicologoId == integracao.ContaPsicologoId &&
                        item.ConversaWhatsAppId == conversa.Id &&
                        item.Status == StatusSessaoBot.Aberta,
                cancellationToken);
            if (sessao is null)
            {
                dbContext.SessoesBot.Add(new SessaoBot
                {
                    Id = Guid.NewGuid(),
                    ContaPsicologoId = integracao.ContaPsicologoId,
                    ConversaWhatsAppId = conversa.Id,
                    PacienteId = conversa.PacienteId,
                    IntencaoAtual = intencao,
                    EtapaAtual = intencao,
                    Status = StatusSessaoBot.Aberta,
                    ExpiraEm = DateTimeOffset.UtcNow.AddHours(2),
                    CriadoEm = DateTimeOffset.UtcNow
                });
            }
            else
            {
                sessao.IntencaoAtual = intencao;
                sessao.AtualizadoEm = DateTimeOffset.UtcNow;
            }

            await ExecutarAcaoBotAsync(integracao, conversa, conteudoBruto, intencao, cancellationToken);
        }

        return conversa.Id;
    }

    private async Task AtualizarStatusMensagemAsync(
        Guid contaId,
        string? mensagemExternaId,
        StatusMensagemWhatsApp status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mensagemExternaId))
            return;

        var mensagem = await dbContext.MensagensWhatsApp.FirstOrDefaultAsync(
            item => item.ContaPsicologoId == contaId && item.MensagemExternaId == mensagemExternaId,
            cancellationToken);
        if (mensagem is null)
            return;

        // Webhooks podem chegar fora de ordem. Uma confirmação antiga não regride leitura/entrega.
        if (mensagem.Status == StatusMensagemWhatsApp.Lida ||
            (mensagem.Status == StatusMensagemWhatsApp.Entregue && status != StatusMensagemWhatsApp.Lida)) return;
        mensagem.Status = status;
        if (status == StatusMensagemWhatsApp.Entregue || status == StatusMensagemWhatsApp.Enviada)
            mensagem.EnviadaEm ??= DateTimeOffset.UtcNow;
        if (status == StatusMensagemWhatsApp.Lida)
            mensagem.LidaEm = DateTimeOffset.UtcNow;
        if (status == StatusMensagemWhatsApp.Falhou)
            mensagem.ErroEnvio = "Falha reportada pelo Bridge.";
    }

    private async Task<Paciente?> TentarIdentificarPacienteAsync(Guid contaId, string telefone, CancellationToken cancellationToken)
    {
        // Paciente.Telefone e criptografado: a busca usa o hash deterministico
        // dos ContatosPaciente (Telefone/WhatsApp normalizados para digitos).
        var hashes = VariacoesHashTelefone(telefone).ToArray();
        var contato = await dbContext.ContatosPaciente.AsNoTracking()
            .Where(item => item.ContaPsicologoId == contaId &&
                           hashes.Contains(item.ValorHash) &&
                           (item.TipoContato == TipoContato.Telefone || item.TipoContato == TipoContato.WhatsApp))
            .OrderByDescending(item => item.Principal)
            .FirstOrDefaultAsync(cancellationToken);

        if (contato is null)
            return null;

        return await dbContext.Pacientes.SingleOrDefaultAsync(
            item => item.Id == contato.PacienteId &&
                    item.ContaPsicologoId == contaId &&
                    item.Status == StatusPaciente.Ativo,
            cancellationToken);
    }

    private IEnumerable<string> VariacoesHashTelefone(string telefone)
    {
        // Variações de discagem BR: com/sem DDI 55 e com/sem nono digito.
        var candidatos = new HashSet<string>(StringComparer.Ordinal) { telefone };
        if (telefone.StartsWith("55", StringComparison.Ordinal) && telefone.Length > 11)
            candidatos.Add(telefone[2..]);
        if (telefone.Length >= 11)
            candidatos.Add(telefone[^11..].TrimStart('0'));
        foreach (var numero in candidatos.ToArray())
        {
            if (numero.Length == 11 && numero[2] == '9')
                candidatos.Add(string.Concat(numero.AsSpan(0, 2), numero.AsSpan(3)));
            if (numero.Length == 10)
                candidatos.Add(string.Concat(numero.AsSpan(0, 2), "9", numero.AsSpan(2)));
        }
        foreach (var numero in candidatos)
            yield return protecao.HashDeterministico(numero);
    }

    private async Task NotificarEquipeAsync(Guid contaId, string titulo, string mensagem, CancellationToken cancellationToken)
    {
        // Quem responde conversas: pessoas ativas com a permissão de conversas (inclui perfis personalizados).
        var equipe = await dbContext.Usuarios.AsNoTracking()
            .Include(item => item.PerfilAcesso)
            .Where(item => item.ContaPsicologoId == contaId && item.Status == StatusUsuario.Ativo)
            .ToArrayAsync(cancellationToken);

        foreach (var usuario in equipe.Where(PermissoesEfetivas.PodeConversar))
            await notificacoes.CriarAsync(contaId, usuario.Id, titulo, mensagem, "whatsapp", cancellationToken);
    }

    private static string DetectarIntencaoAdministrativa(string? conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo))
            return "desconhecida";

        var normalizado = TextoNormalizado.ParaComparacao(conteudo);
        if (TermosFalarComHumano.Any(termo => normalizado.Contains(termo, StringComparison.Ordinal)))
            return IntencaoFalarComHumano;

        var texto = conteudo.ToLowerInvariant();
        if (texto.Contains("agendar") || texto.Contains("marcar consulta")) return "agendar_consulta";
        if (texto.Contains("cancelar")) return "cancelar_consulta";
        if (texto.Contains("remarcar") || texto.Contains("reagendar")) return "remarcar_consulta";
        if (texto.Contains("confirmar")) return "confirmar_presenca";
        if (texto.Contains("horario") || texto.Contains("horário")) return "consultar_horarios";
        return "administrativa_geral";
    }

    private static string NormalizarTelefone(string telefone)
    {
        var digitos = DigitosRegex().Replace(telefone ?? string.Empty, string.Empty);
        if (digitos.Length < 8)
            throw new RegraNegocioException("Telefone do evento Bridge inválido.");
        return digitos;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitosRegex();
}

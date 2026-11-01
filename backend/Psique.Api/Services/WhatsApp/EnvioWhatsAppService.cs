using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Psique.Api.Data;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Concorrencia;
using Psique.Api.Shared.Enums;

namespace Psique.Api.Services.WhatsApp;

public sealed class EnvioWhatsAppService(AppDbContext db, EvolutionApiClient transporte, IProtecaoDadosService protecao)
{
    public async Task<int> ProcessarPendentesAsync(CancellationToken ct)
    {
        var ids = await db.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.Direcao == DirecaoMensagem.Saida && m.Status == StatusMensagemWhatsApp.Pendente &&
                        m.EnvioIniciadoEm == null && m.MensagemExternaId == null &&
                        m.ContaPsicologo.Status == StatusContaPsicologo.Ativa &&
                        m.ConversaWhatsApp.IntegracaoWhatsApp.Ativo &&
                        m.ConversaWhatsApp.IntegracaoWhatsApp.Status == StatusIntegracao.Ativa)
            .OrderBy(m => m.CriadoEm).Select(m => m.Id).Take(50).ToArrayAsync(ct);
        foreach (var id in ids) await ProcessarAsync(id, ct);
        return ids.Length;
    }

    public async Task ProcessarAsync(Guid id, CancellationToken ct)
    {
        // O primeiro identificador vem somente da fila interna, nunca de um usuário.
        await using var trava = await TravaOperacao.AdquirirAsync(db, $"mensagem-whatsapp:{id:N}", ct);
        var mensagem = await db.MensagensWhatsApp.Include(m => m.ConversaWhatsApp).ThenInclude(c => c.IntegracaoWhatsApp)
            .SingleOrDefaultAsync(m => m.Id == id, ct);
        if (mensagem is null) return;
        await db.Entry(mensagem).ReloadAsync(ct);
        if (mensagem.EnvioIniciadoEm is not null || mensagem.Status != StatusMensagemWhatsApp.Pendente || mensagem.MensagemExternaId is not null) return;
        var conversa = mensagem.ConversaWhatsApp;
        await db.Entry(conversa).ReloadAsync(ct);
        var integracao = conversa.IntegracaoWhatsApp;
        await db.Entry(integracao).ReloadAsync(ct);
        var ativa = await db.ContasPsicologo.AsNoTracking().AnyAsync(c => c.Id == mensagem.ContaPsicologoId && c.Status == StatusContaPsicologo.Ativa, ct);
        if (!ativa || !integracao.Ativo || integracao.Status != StatusIntegracao.Ativa) return;

        // A trava do lembrete também coordena o cancelamento manual com a entrega.
        await using var travaLembrete = mensagem.LembreteId is { } lembreteId
            ? await TravaOperacao.AdquirirAsync(db, $"lembrete:{lembreteId:N}", ct) : null;
        var lembrete = mensagem.LembreteId is { } vinculo
            ? await db.Lembretes.Include(l => l.Consulta).SingleAsync(l => l.Id == vinculo && l.ContaPsicologoId == mensagem.ContaPsicologoId, ct) : null;
        if (lembrete is not null)
        {
            await db.Entry(lembrete).ReloadAsync(ct);
            await db.Entry(lembrete.Consulta).ReloadAsync(ct);
        }
        if (conversa.Status is StatusConversaWhatsApp.Finalizada or StatusConversaWhatsApp.Bloqueada ||
            (lembrete is not null && (lembrete.Status != StatusLembrete.Agendado ||
             lembrete.Consulta.InicioEm <= DateTimeOffset.UtcNow ||
             lembrete.Consulta.Status is not (StatusConsulta.AguardandoConfirmacao or StatusConsulta.Agendada or StatusConsulta.Confirmada))))
        {
            mensagem.Status = StatusMensagemWhatsApp.Falhou;
            mensagem.ErroEnvio = "Envio encerrado: a conversa ou a consulta não permite mais esta mensagem.";
            if (lembrete?.Status == StatusLembrete.Agendado)
            {
                lembrete.Status = StatusLembrete.Ignorado;
                lembrete.ErroEnvio = mensagem.ErroEnvio;
                lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return;
        }

        // Persiste ANTES da chamada externa. Em caso de queda, não reenvia às cegas:
        // o provedor não oferece chave de idempotência neste contrato de envio.
        mensagem.EnvioIniciadoEm = DateTimeOffset.UtcNow;
        mensagem.ErroEnvio = "Envio iniciado. Se a confirmação não chegar, confira no provedor antes de reenviar.";
        await db.SaveChangesAsync(ct);
        try
        {
            mensagem.MensagemExternaId = await transporte.EnviarTextoAsync(integracao, conversa.Telefone,
                protecao.Desproteger(mensagem.Conteudo ?? string.Empty), ct);
            await using var confirmacao = await TravaOperacao.AdquirirAsync(db,
                $"confirmacao-whatsapp:{mensagem.ContaPsicologoId:N}:{mensagem.MensagemExternaId}", ct);
            mensagem.Status = StatusMensagemWhatsApp.Enviada;
            mensagem.EnviadaEm = DateTimeOffset.UtcNow;
            mensagem.ErroEnvio = null;
            if (lembrete is not null)
            {
                lembrete.Status = StatusLembrete.Enviado;
                lembrete.EnviadoEm = mensagem.EnviadaEm;
                lembrete.ErroEnvio = null;
                lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            }
            var confirmacoes = await db.EventosBridgeWhatsApp.AsNoTracking().Where(e =>
                e.ContaPsicologoId == mensagem.ContaPsicologoId && e.IntegracaoWhatsAppId == integracao.Id &&
                e.MensagemExternaId == mensagem.MensagemExternaId && e.StatusMensagem != null && e.Processado)
                .Select(e => e.StatusMensagem).ToArrayAsync(ct);
            if (confirmacoes.Contains(StatusMensagemWhatsApp.Lida))
            {
                mensagem.Status = StatusMensagemWhatsApp.Lida;
                mensagem.LidaEm = DateTimeOffset.UtcNow;
            }
            else if (confirmacoes.Contains(StatusMensagemWhatsApp.Entregue)) mensagem.Status = StatusMensagemWhatsApp.Entregue;
            else if (confirmacoes.Contains(StatusMensagemWhatsApp.Falhou))
            {
                mensagem.Status = StatusMensagemWhatsApp.Falhou;
                mensagem.ErroEnvio = "Falha de entrega informada pelo WhatsApp.";
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            mensagem.Status = StatusMensagemWhatsApp.Falhou;
            mensagem.ErroEnvio = "O envio não foi confirmado. Confira no provedor antes de reenviar para evitar duplicação.";
            if (lembrete is not null)
            {
                lembrete.Status = StatusLembrete.Falhou;
                lembrete.ErroEnvio = mensagem.ErroEnvio;
                lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            }
        }
        // Mesmo no encerramento do processo, tenta persistir o resultado conhecido.
        await db.SaveChangesAsync(CancellationToken.None);
    }
}

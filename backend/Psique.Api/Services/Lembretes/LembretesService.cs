using System.Globalization;
using Psique.Api.Configuracoes;
using Psique.Api.Data;
using Psique.Api.DTOs.Lembretes;
using Psique.Api.Models;
using Psique.Api.Services.Comunicacao;
using Psique.Api.Services.ModelosMensagem;
using Psique.Api.Services.Notificacoes;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Psique.Api.Shared.Paginacao;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Psique.Api.Services.Lembretes;

public sealed class LembretesService(
    AppDbContext dbContext,
    IAutorizacaoContaService autorizacao,
    IAuditoriaService auditoria,
    IModeloMensagemTemplateService templateService,
    IEmailTransacionalService emailTransacional,
    IOptions<EmailConfiguracao> emailConfiguracao,
    INotificacoesService notificacoes,
    IProtecaoDadosService protecaoDados) : ILembretesService
{
    private static readonly HashSet<CanalComunicacao> CanaisOperacionais =
    [
        CanalComunicacao.Email,
        CanalComunicacao.WhatsApp
    ];

    private static readonly HashSet<StatusConsulta> StatusConsultaPermitidos =
    [
        StatusConsulta.AguardandoConfirmacao,
        StatusConsulta.Agendada,
        StatusConsulta.Confirmada
    ];

    public async Task<ResultadoPaginado<LembreteResponse>> ListarAsync(
        Guid usuarioId,
        Guid contaId,
        FiltroLembreteRequest filtro,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        ValidarPeriodo(filtro);
        var (pagina, tamanhoPagina) = ParametrosPaginacao.Normalizar(filtro.Pagina, filtro.TamanhoPagina);

        var consulta = MontarConsultaBase(contaId, filtro);
        var total = await consulta.CountAsync(cancellationToken);
        var itens = await consulta
            .OrderByDescending(item => item.AgendadoPara)
            .ThenByDescending(item => item.CriadoEm)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToArrayAsync(cancellationToken);

        return new ResultadoPaginado<LembreteResponse>(itens.Select(Mapear).ToArray(), total, pagina, tamanhoPagina);
    }

    public async Task<LembreteIndicadoresResponse> IndicadoresAsync(
        Guid usuarioId,
        Guid contaId,
        FiltroLembreteRequest filtro,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        ValidarPeriodo(filtro);

        var filtroIndicadores = new FiltroLembreteRequest
        {
            PeriodoInicio = filtro.PeriodoInicio,
            PeriodoFim = filtro.PeriodoFim,
            PacienteId = filtro.PacienteId,
            Tipo = filtro.Tipo,
            Canal = filtro.Canal
        };
        var baseFiltro = MontarConsultaBase(contaId, filtroIndicadores);
        var programados = await baseFiltro.CountAsync(item => item.Status == StatusLembrete.Agendado, cancellationToken);
        var enviados = await baseFiltro.CountAsync(item => item.Status == StatusLembrete.Enviado, cancellationToken);
        var falhos = await baseFiltro.CountAsync(item => item.Status == StatusLembrete.Falhou, cancellationToken);
        var cancelados = await baseFiltro.CountAsync(item => item.Status == StatusLembrete.Cancelado, cancellationToken);

        return new LembreteIndicadoresResponse(programados, enviados, falhos, cancelados);
    }

    public async Task<LembreteResponse> ObterAsync(
        Guid usuarioId,
        Guid contaId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        var lembrete = await ObterLembreteAsync(contaId, id, cancellationToken, rastrear: false);
        return Mapear(lembrete);
    }

    public async Task<LembreteResponse> CriarAsync(
        Guid usuarioId,
        Guid contaId,
        CriarLembreteRequest request,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        ValidarCanal(request.Canal);

        var consulta = await dbContext.Consultas
            .Include(item => item.Paciente)
            .SingleOrDefaultAsync(
                item => item.Id == request.ConsultaId && item.ContaPsicologoId == contaId,
                cancellationToken)
            ?? throw new RegraNegocioException("Consulta não encontrada.", StatusCodes.Status404NotFound);

        if (!StatusConsultaPermitidos.Contains(consulta.Status))
            throw new RegraNegocioException("Só é possível programar lembrete para consultas ativas na agenda.");

        ModeloMensagem? modelo = null;
        if (request.ModeloMensagemId is not null)
        {
            modelo = await dbContext.ModelosMensagem.SingleOrDefaultAsync(
                item => item.Id == request.ModeloMensagemId &&
                        item.ContaPsicologoId == contaId &&
                        item.Ativo,
                cancellationToken)
                ?? throw new RegraNegocioException("Modelo de mensagem ativo não encontrado.", StatusCodes.Status404NotFound);

            if (modelo.Canal != request.Canal)
                throw new RegraNegocioException("O canal do lembrete deve coincidir com o canal do modelo.");
            if (modelo.Tipo != request.Tipo)
                throw new RegraNegocioException("O tipo do lembrete deve coincidir com o tipo do modelo.");
        }

        var valores = await MontarValoresAsync(contaId, consulta, cancellationToken);
        var (assunto, mensagem) = MontarConteudo(request.Assunto, request.Mensagem, modelo, valores);

        if (request.Canal == CanalComunicacao.Email && string.IsNullOrWhiteSpace(assunto))
            throw new RegraNegocioException("O assunto é obrigatório para lembretes por e-mail.");

        if (string.IsNullOrWhiteSpace(mensagem))
            throw new RegraNegocioException("A mensagem do lembrete é obrigatória.");

        var agendadoPara = request.AgendadoPara.ToUniversalTime();
        if (agendadoPara < DateTimeOffset.UtcNow.AddMinutes(-5))
            throw new RegraNegocioException("A data de agendamento do lembrete não pode estar no passado.");
        if (agendadoPara >= consulta.InicioEm)
            throw new RegraNegocioException("Programe o lembrete para antes do início da consulta.");

        var duplicado = await dbContext.Lembretes.AnyAsync(
            item => item.ContaPsicologoId == contaId &&
                    item.ConsultaId == consulta.Id &&
                    item.Tipo == request.Tipo &&
                    item.Canal == request.Canal &&
                    item.Status == StatusLembrete.Agendado,
            cancellationToken);
        if (duplicado)
            throw new RegraNegocioException("Já existe um lembrete programado para esta consulta, tipo e canal.");

        var lembrete = new Lembrete
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            ConsultaId = consulta.Id,
            PacienteId = consulta.PacienteId,
            ModeloMensagemId = modelo?.Id,
            Canal = request.Canal,
            Tipo = request.Tipo,
            Status = StatusLembrete.Agendado,
            AgendadoPara = agendadoPara,
            Assunto = assunto,
            Mensagem = mensagem
        };

        dbContext.Lembretes.Add(lembrete);
        auditoria.Registrar(
            contaId,
            usuarioId,
            "LembreteCriado",
            nameof(Lembrete),
            lembrete.Id,
            "Lembrete administrativo programado.",
            null,
            new
            {
                lembrete.ConsultaId,
                lembrete.PacienteId,
                lembrete.Tipo,
                lembrete.Canal,
                lembrete.AgendadoPara,
                Assunto = assunto
            },
            ip,
            userAgent);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Processamento síncrono apenas quando o horário já venceu e o canal permite.
        if (lembrete.AgendadoPara <= DateTimeOffset.UtcNow)
            await TentarProcessarEnvioAsync(lembrete, consulta, assunto, usuarioId, ip, userAgent, cancellationToken);

        lembrete.Consulta = consulta;
        lembrete.Paciente = consulta.Paciente;
        lembrete.ModeloMensagem = modelo;
        return Mapear(lembrete);
    }

    public async Task<PreviaLembreteResponse> PreviaAsync(
        Guid usuarioId,
        Guid contaId,
        PreviaLembreteRequest request,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        ValidarCanal(request.Canal);

        var consulta = await dbContext.Consultas
            .AsNoTracking()
            .Include(item => item.Paciente)
            .SingleOrDefaultAsync(
                item => item.Id == request.ConsultaId && item.ContaPsicologoId == contaId,
                cancellationToken)
            ?? throw new RegraNegocioException("Consulta não encontrada.", StatusCodes.Status404NotFound);

        ModeloMensagem? modelo = null;
        if (request.ModeloMensagemId is not null)
        {
            modelo = await dbContext.ModelosMensagem.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == request.ModeloMensagemId &&
                        item.ContaPsicologoId == contaId &&
                        item.Ativo,
                cancellationToken)
                ?? throw new RegraNegocioException("Modelo de mensagem ativo não encontrado.", StatusCodes.Status404NotFound);
        }

        var valores = await MontarValoresAsync(contaId, consulta, cancellationToken);
        var (assunto, mensagem) = MontarConteudo(request.Assunto, request.Mensagem, modelo, valores);
        return new PreviaLembreteResponse(
            string.IsNullOrWhiteSpace(assunto) ? null : assunto,
            mensagem,
            request.Canal,
            request.Tipo);
    }

    public async Task<LembreteResponse> CancelarAsync(
        Guid usuarioId,
        Guid contaId,
        Guid id,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await using var trava = await Shared.Concorrencia.TravaOperacao.AdquirirAsync(dbContext, $"lembrete:{id:N}", cancellationToken);
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        var lembrete = await ObterLembreteAsync(contaId, id, cancellationToken, rastrear: true);

        if (lembrete.Status is not (StatusLembrete.Agendado or StatusLembrete.Falhou))
            throw new RegraNegocioException("Somente lembretes programados ou com falha podem ser cancelados.");

        var statusAnterior = lembrete.Status;
        lembrete.Status = StatusLembrete.Cancelado;
        lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
        lembrete.ErroEnvio = null;

        auditoria.Registrar(
            contaId,
            usuarioId,
            "LembreteCancelado",
            nameof(Lembrete),
            lembrete.Id,
            "Lembrete administrativo cancelado.",
            new { Status = statusAnterior },
            new { lembrete.Status },
            ip,
            userAgent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Mapear(lembrete);
    }

    public async Task<LembreteResponse> TentarNovamenteAsync(
        Guid usuarioId,
        Guid contaId,
        Guid id,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await using var trava = await Shared.Concorrencia.TravaOperacao.AdquirirAsync(dbContext, $"lembrete:{id:N}", cancellationToken);
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        var lembrete = await ObterLembreteAsync(contaId, id, cancellationToken, rastrear: true);

        if (lembrete.Status != StatusLembrete.Falhou)
            throw new RegraNegocioException("Somente lembretes com falha podem ser reprocessados.");

        if (lembrete.Canal == CanalComunicacao.Email && lembrete.EnvioEmailIniciadoEm is not null)
            throw new RegraNegocioException("Confira o envio no serviço de e-mail antes de criar um novo lembrete. Esta tentativa não será repetida para evitar duplicação.");

        if (lembrete.Canal is CanalComunicacao.Sms)
            throw new RegraNegocioException("Canal indisponível nesta etapa. Reprocesse com e-mail quando o lembrete for recriado.");

        if (lembrete.Canal == CanalComunicacao.WhatsApp && await dbContext.MensagensWhatsApp.AnyAsync(
            m => m.ContaPsicologoId == contaId && m.LembreteId == id && m.EnvioIniciadoEm != null, cancellationToken))
            throw new RegraNegocioException("Confira o envio no provedor antes de criar uma nova mensagem. Esta tentativa não será repetida automaticamente.");

        var statusAnterior = lembrete.Status;
        lembrete.Status = StatusLembrete.Agendado;
        lembrete.ErroEnvio = null;
        lembrete.AgendadoPara = DateTimeOffset.UtcNow;
        lembrete.AtualizadoEm = DateTimeOffset.UtcNow;

        auditoria.Registrar(
            contaId,
            usuarioId,
            "LembreteReprocessamentoSolicitado",
            nameof(Lembrete),
            lembrete.Id,
            "Reprocessamento de lembrete solicitado.",
            new { Status = statusAnterior },
            new { lembrete.Status, lembrete.AgendadoPara },
            ip,
            userAgent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await TentarProcessarEnvioAsync(
            lembrete,
            lembrete.Consulta,
            lembrete.Assunto,
            usuarioId,
            ip,
            userAgent,
            cancellationToken);

        return Mapear(lembrete);
    }

    private IQueryable<Lembrete> MontarConsultaBase(Guid contaId, FiltroLembreteRequest filtro)
    {
        var consulta = dbContext.Lembretes
            .AsNoTracking()
            .Include(item => item.Paciente)
            .Include(item => item.Consulta)
            .Include(item => item.ModeloMensagem)
            .Where(item => item.ContaPsicologoId == contaId);

        if (filtro.PeriodoInicio is not null)
            consulta = consulta.Where(item => item.AgendadoPara >= filtro.PeriodoInicio.Value.ToUniversalTime());
        if (filtro.PeriodoFim is not null)
            consulta = consulta.Where(item => item.AgendadoPara <= filtro.PeriodoFim.Value.ToUniversalTime());
        if (filtro.Status is not null)
            consulta = consulta.Where(item => item.Status == filtro.Status);
        if (filtro.PacienteId is not null)
            consulta = consulta.Where(item => item.PacienteId == filtro.PacienteId);
        if (filtro.ConsultaId is not null)
            consulta = consulta.Where(item => item.ConsultaId == filtro.ConsultaId);
        if (filtro.Tipo is not null)
            consulta = consulta.Where(item => item.Tipo == filtro.Tipo);
        if (filtro.Canal is not null)
            consulta = consulta.Where(item => item.Canal == filtro.Canal);

        return consulta;
    }

    public async Task<LembreteResponse?> ProgramarAutomaticoParaConsultaAsync(
        Guid? usuarioId,
        Guid contaId,
        Guid consultaId,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var configuracao = await dbContext.ConfiguracoesAtendimento.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ContaPsicologoId == contaId && item.Ativo, cancellationToken);
        if (configuracao is null || !configuracao.LembretesAutomaticosAtivos)
            return null;

        var canal = configuracao.CanalLembretePadrao;
        if (canal is not (CanalComunicacao.Email or CanalComunicacao.WhatsApp))
            canal = CanalComunicacao.Email;

        var consulta = await dbContext.Consultas
            .Include(item => item.Paciente)
            .SingleOrDefaultAsync(
                item => item.Id == consultaId && item.ContaPsicologoId == contaId,
                cancellationToken);
        if (consulta is null)
            return null;

        if (!StatusConsultaPermitidos.Contains(consulta.Status))
            return null;

        var jaProgramado = await dbContext.Lembretes.AnyAsync(
            item => item.ContaPsicologoId == contaId &&
                    item.ConsultaId == consulta.Id &&
                    item.Tipo == TipoLembrete.LembreteConsulta &&
                    item.Canal == canal &&
                    item.Status == StatusLembrete.Agendado,
            cancellationToken);
        if (jaProgramado)
            return null;

        var modelo = await dbContext.ModelosMensagem.AsNoTracking()
            .Where(item => item.ContaPsicologoId == contaId &&
                           item.Ativo &&
                           item.Tipo == TipoLembrete.LembreteConsulta &&
                           item.Canal == canal)
            .OrderByDescending(item => item.AtualizadoEm ?? item.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        var valores = await MontarValoresAsync(contaId, consulta, cancellationToken);
        const string corpoPadrao =
            "Olá, {{nome_paciente}}.\n\n" +
            "Lembrete da sua consulta em {{data_consulta}} às {{horario_consulta}} ({{modalidade}}).\n" +
            "Profissional: {{nome_profissional}}.\n" +
            "Endereço: {{endereco}}\n" +
            "Link online: {{link_sessao_online}}\n" +
            "{{regras_cancelamento}}\n" +
            "{{regras_remarcacao}}";
        const string assuntoPadrao = "Lembrete de consulta - {{data_consulta}}";

        var (assunto, mensagem) = MontarConteudo(
            canal == CanalComunicacao.Email ? (modelo?.Assunto ?? assuntoPadrao) : null,
            modelo?.Conteudo ?? corpoPadrao,
            modelo,
            valores);

        if (canal == CanalComunicacao.Email && string.IsNullOrWhiteSpace(assunto))
            assunto = templateService.RenderizarAssunto(assuntoPadrao, valores);

        var horas = Math.Clamp(configuracao.AntecedenciaLembreteHoras, 0, 720);
        var agendadoPara = consulta.InicioEm.ToUniversalTime().AddHours(-horas);
        if (agendadoPara < DateTimeOffset.UtcNow)
            agendadoPara = DateTimeOffset.UtcNow;

        // Não programa se a consulta já passou.
        if (consulta.InicioEm.ToUniversalTime() <= DateTimeOffset.UtcNow)
            return null;

        var lembrete = new Lembrete
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            ConsultaId = consulta.Id,
            PacienteId = consulta.PacienteId,
            ModeloMensagemId = modelo?.Id,
            Canal = canal,
            Tipo = TipoLembrete.LembreteConsulta,
            Status = StatusLembrete.Agendado,
            AgendadoPara = agendadoPara,
            Assunto = assunto,
            Mensagem = mensagem
        };

        dbContext.Lembretes.Add(lembrete);
        auditoria.Registrar(
            contaId,
            usuarioId,
            "LembreteAutomaticoProgramado",
            nameof(Lembrete),
            lembrete.Id,
            "Lembrete automático programado para a consulta.",
            null,
            new { lembrete.ConsultaId, lembrete.Canal, lembrete.AgendadoPara, Automatico = true },
            ip,
            userAgent);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (lembrete.AgendadoPara <= DateTimeOffset.UtcNow)
            await TentarProcessarEnvioAsync(lembrete, consulta, assunto, usuarioId, ip, userAgent, cancellationToken);

        lembrete.Consulta = consulta;
        lembrete.Paciente = consulta.Paciente;
        lembrete.ModeloMensagem = modelo;
        return Mapear(lembrete);
    }

    public async Task<int> EncerrarProgramadosDaConsultaAsync(
        Guid? usuarioId,
        Guid contaId,
        Guid consultaId,
        StatusLembrete statusFinal,
        string motivo,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (statusFinal is not (StatusLembrete.Cancelado or StatusLembrete.Ignorado))
            throw new RegraNegocioException("Status final de encerramento inválido.");

        var programados = await dbContext.Lembretes
            .Where(item => item.ContaPsicologoId == contaId &&
                           item.ConsultaId == consultaId &&
                           item.Status == StatusLembrete.Agendado)
            .ToListAsync(cancellationToken);

        if (programados.Count == 0)
            return 0;

        foreach (var lembrete in programados)
        {
            var anterior = lembrete.Status;
            lembrete.Status = statusFinal;
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            lembrete.ErroEnvio = motivo;
            auditoria.Registrar(
                contaId,
                usuarioId,
                "LembreteEncerradoPorConsulta",
                nameof(Lembrete),
                lembrete.Id,
                motivo,
                new { Status = anterior },
                new { lembrete.Status, Motivo = motivo },
                ip,
                userAgent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return programados.Count;
    }

    public async Task<int> ProcessarVencidosAsync(CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        var vencidos = await dbContext.Lembretes
            .Include(item => item.Consulta)
                .ThenInclude(consulta => consulta.Paciente)
            .Include(item => item.Paciente)
            // Quem já foi tentado (e-mail desligado, canal interno) volta a cada 15 minutos, não a cada ciclo:
            // assim lembretes travados não ocupam o lote e escondem os novos.
            .Where(item => item.ContaPsicologo.Status == StatusContaPsicologo.Ativa &&
                           item.Status == StatusLembrete.Agendado && item.AgendadoPara <= agora &&
                           (item.EnvioEmailIniciadoEm != null || item.AtualizadoEm == null || item.AtualizadoEm <= agora.AddMinutes(-15)))
            .OrderBy(item => item.AgendadoPara)
            .Take(50)
            .ToListAsync(cancellationToken);

        var processados = 0;
        foreach (var lembrete in vencidos)
        {
            await TentarProcessarEnvioAsync(
                lembrete,
                lembrete.Consulta,
                lembrete.Assunto,
                usuarioId: null,
                ip: null,
                userAgent: null,
                cancellationToken);
            processados++;
        }

        return processados;
    }

    private async Task<Lembrete> ObterLembreteAsync(
        Guid contaId,
        Guid id,
        CancellationToken cancellationToken,
        bool rastrear)
    {
        var consulta = rastrear
            ? dbContext.Lembretes.AsQueryable()
            : dbContext.Lembretes.AsNoTracking();

        return await consulta
                   .Include(item => item.Paciente)
                   .Include(item => item.Consulta)
                   .Include(item => item.ModeloMensagem)
                   .SingleOrDefaultAsync(item => item.Id == id && item.ContaPsicologoId == contaId, cancellationToken)
               ?? throw new RegraNegocioException("Lembrete não encontrado.", StatusCodes.Status404NotFound);
    }

    private (string? Assunto, string Mensagem) MontarConteudo(
        string? assuntoInformado,
        string? mensagemInformada,
        ModeloMensagem? modelo,
        IReadOnlyDictionary<string, string?> valores)
    {
        string? assuntoTemplate = assuntoInformado;
        string? mensagemTemplate = mensagemInformada;

        if (modelo is not null)
        {
            assuntoTemplate = string.IsNullOrWhiteSpace(assuntoInformado) ? modelo.Assunto : assuntoInformado;
            mensagemTemplate = string.IsNullOrWhiteSpace(mensagemInformada) ? modelo.Conteudo : mensagemInformada;
        }

        if (string.IsNullOrWhiteSpace(mensagemTemplate))
            throw new RegraNegocioException("Informe a mensagem ou selecione um modelo.");

        templateService.ValidarConteudo(mensagemTemplate, assuntoTemplate);
        var assunto = string.IsNullOrWhiteSpace(assuntoTemplate)
            ? null
            : templateService.RenderizarAssunto(assuntoTemplate, valores);
        var mensagem = templateService.Renderizar(mensagemTemplate, valores);

        if (mensagem.Length > 2000)
            throw new RegraNegocioException("A mensagem renderizada excede o limite de 2000 caracteres.");

        return (string.IsNullOrWhiteSpace(assunto) ? null : assunto, mensagem);
    }

    private async Task<IReadOnlyDictionary<string, string?>> MontarValoresAsync(
        Guid contaId,
        Consulta consulta,
        CancellationToken cancellationToken)
    {
        var cultura = CultureInfo.GetCultureInfo("pt-BR");
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var profissional = await dbContext.DadosProfissionaisPsicologo
            .AsNoTracking()
            .Where(item => item.ContaPsicologoId == contaId && item.Status == StatusUsuario.Ativo)
            .OrderBy(item => item.CriadoEm)
            .Select(item => item.NomeProfissional)
            .FirstOrDefaultAsync(cancellationToken);
        valores[VariaveisModeloMensagem.NomeProfissional] = string.IsNullOrWhiteSpace(profissional)
            ? "Profissional"
            : profissional;

        var configuracao = await dbContext.ConfiguracoesAtendimento
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ContaPsicologoId == contaId && item.Ativo, cancellationToken);

        ModelosMensagemService.PreencherValoresConsulta(valores, consulta, cultura, Agenda.FusoHorarioConta.Resolver(configuracao?.FusoHorario));

        if (configuracao is null)
        {
            valores[VariaveisModeloMensagem.RegrasCancelamento] = "Consulte a recepção para cancelamentos.";
            valores[VariaveisModeloMensagem.RegrasRemarcacao] = "Consulte a recepção para remarcações.";
        }
        else
        {
            valores[VariaveisModeloMensagem.RegrasCancelamento] =
                configuracao.AntecedenciaCancelamentoHoras is null or <= 0
                    ? "Cancelamentos conforme orientação do consultório."
                    : $"Cancelamentos com antecedência mínima de {configuracao.AntecedenciaCancelamentoHoras} hora(s)" +
                      (configuracao.ExigeJustificativaCancelamento ? ", com justificativa." : ".");
            valores[VariaveisModeloMensagem.RegrasRemarcacao] =
                configuracao.AntecedenciaRemarcacaoHoras is null or <= 0
                    ? "Remarcações conforme orientação do consultório."
                    : $"Remarcações com antecedência mínima de {configuracao.AntecedenciaRemarcacaoHoras} hora(s).";
        }

        return valores;
    }

    private async Task TentarProcessarEnvioAsync(
        Lembrete lembrete,
        Consulta consulta,
        string? assunto,
        Guid? usuarioId,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await using var trava = await Shared.Concorrencia.TravaOperacao.AdquirirAsync(dbContext, $"lembrete:{lembrete.Id:N}", cancellationToken);
        await dbContext.Entry(lembrete).ReloadAsync(cancellationToken);
        await dbContext.Entry(consulta).ReloadAsync(cancellationToken);
        var contaAtiva = await dbContext.ContasPsicologo.AsNoTracking()
            .AnyAsync(item => item.Id == lembrete.ContaPsicologoId && item.Status == StatusContaPsicologo.Ativa, cancellationToken);
        if (!contaAtiva) return;
        if (lembrete.Status != StatusLembrete.Agendado)
            return;

        if (lembrete.Canal == CanalComunicacao.Email && lembrete.EnvioEmailIniciadoEm is not null)
        {
            lembrete.Status = StatusLembrete.Falhou;
            lembrete.ErroEnvio = "O envio anterior não foi confirmado. Confira no serviço de e-mail antes de criar um novo lembrete para evitar duplicação.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            auditoria.Registrar(lembrete.ContaPsicologoId, usuarioId, "LembreteEmailSemConfirmacao", nameof(Lembrete),
                lembrete.Id, "Tentativa de e-mail interrompida; novo envio exige conferência.", null, new { lembrete.Status }, ip, userAgent);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Todas as entradas (criação imediata, reenvio e worker) conferem a consulta
        // novamente antes de entregar a mensagem ao transporte.
        if (!StatusConsultaPermitidos.Contains(consulta.Status) || consulta.InicioEm <= DateTimeOffset.UtcNow)
        {
            lembrete.Status = StatusLembrete.Ignorado;
            lembrete.ErroEnvio = "A consulta já começou ou não está mais ativa; lembrete encerrado.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            auditoria.Registrar(lembrete.ContaPsicologoId, usuarioId, "LembreteEncerradoPorConsulta", nameof(Lembrete),
                lembrete.Id, "Lembrete encerrado porque a consulta não permite mais o envio.", null, new { lembrete.Status }, ip, userAgent);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (lembrete.Canal == CanalComunicacao.WhatsApp)
        {
            await TentarEntregarWhatsAppAsync(lembrete, consulta, usuarioId, ip, userAgent, cancellationToken);
            return;
        }

        if (lembrete.Canal != CanalComunicacao.Email)
        {
            lembrete.Status = StatusLembrete.Falhou;
            lembrete.ErroEnvio = "Canal de envio indisponível nesta etapa.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            auditoria.Registrar(
                lembrete.ContaPsicologoId,
                usuarioId,
                "LembreteFalhaEnvio",
                nameof(Lembrete),
                lembrete.Id,
                "Falha no processamento do lembrete.",
                null,
                new { lembrete.Status, lembrete.ErroEnvio },
                ip,
                userAgent);
            await dbContext.SaveChangesAsync(cancellationToken);
            await NotificarFalhaLembreteAsync(lembrete, cancellationToken);
            return;
        }

        var config = emailConfiguracao.Value;
        if (!config.Ativo)
        {
            // Não finge envio: mantém programado com bloqueio técnico explícito.
            lembrete.ErroEnvio = "Envio de e-mail desabilitado na configuração do servidor. O lembrete permanece programado.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var destinatario = string.IsNullOrWhiteSpace(consulta.Paciente.Email)
            ? null
            : protecaoDados.Desproteger(consulta.Paciente.Email);
        if (string.IsNullOrWhiteSpace(destinatario))
        {
            lembrete.Status = StatusLembrete.Falhou;
            lembrete.ErroEnvio = "Paciente sem e-mail cadastrado.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            auditoria.Registrar(
                lembrete.ContaPsicologoId,
                usuarioId,
                "LembreteFalhaEnvio",
                nameof(Lembrete),
                lembrete.Id,
                "Falha no envio do lembrete por e-mail.",
                null,
                new { lembrete.Status, lembrete.ErroEnvio },
                ip,
                userAgent);
            await dbContext.SaveChangesAsync(cancellationToken);
            await NotificarFalhaLembreteAsync(lembrete, cancellationToken);
            return;
        }

        // O aceite SMTP pode ocorrer antes de uma queda do processo. Registra a
        // tentativa antes do transporte para impedir repetição cega após reinício.
        lembrete.EnvioEmailIniciadoEm = DateTimeOffset.UtcNow;
        lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
        lembrete.ErroEnvio = "Envio iniciado. Se não houver confirmação, confira no serviço de e-mail antes de criar outro lembrete.";
        await dbContext.SaveChangesAsync(cancellationToken);
        var enviado = await emailTransacional.EnviarMensagemAdministrativaAsync(
            destinatario,
            consulta.Paciente.NomeCompleto,
            assunto ?? "Lembrete de consulta",
            lembrete.Mensagem ?? string.Empty,
            cancellationToken);

        if (enviado)
        {
            lembrete.Status = StatusLembrete.Enviado;
            lembrete.EnviadoEm = DateTimeOffset.UtcNow;
            lembrete.ErroEnvio = null;
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            auditoria.Registrar(
                lembrete.ContaPsicologoId,
                usuarioId,
                "LembreteEnviado",
                nameof(Lembrete),
                lembrete.Id,
                "Lembrete administrativo enviado por e-mail.",
                null,
                new { lembrete.Status, lembrete.EnviadoEm },
                ip,
                userAgent);
        }
        else
        {
            lembrete.Status = StatusLembrete.Falhou;
            lembrete.ErroEnvio = "O envio não foi confirmado. Confira no serviço de e-mail antes de criar um novo lembrete para evitar duplicação.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            auditoria.Registrar(
                lembrete.ContaPsicologoId,
                usuarioId,
                "LembreteFalhaEnvio",
                nameof(Lembrete),
                lembrete.Id,
                "Falha no envio do lembrete por e-mail.",
                null,
                new { lembrete.Status, lembrete.ErroEnvio },
                ip,
                userAgent);
            await dbContext.SaveChangesAsync(cancellationToken);
            await NotificarFalhaLembreteAsync(lembrete, cancellationToken);
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TentarEntregarWhatsAppAsync(
        Lembrete lembrete,
        Consulta consulta,
        Guid? usuarioId,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var integracaoAtiva = await dbContext.IntegracoesWhatsApp.AsNoTracking().AnyAsync(
            item => item.ContaPsicologoId == lembrete.ContaPsicologoId && item.Ativo && item.Status == StatusIntegracao.Ativa,
            cancellationToken);
        if (!integracaoAtiva)
        {
            lembrete.ErroEnvio = "Integração WhatsApp inativa. O lembrete permanece programado.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var conversa = await dbContext.ConversasWhatsApp
            .Where(item => item.ContaPsicologoId == lembrete.ContaPsicologoId &&
                           item.PacienteId == consulta.PacienteId &&
                           item.Status != StatusConversaWhatsApp.Finalizada &&
                           item.Status != StatusConversaWhatsApp.Bloqueada)
            .OrderByDescending(item => item.UltimaMensagemEm ?? item.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);
        if (conversa is null)
        {
            lembrete.ErroEnvio = "Paciente sem conversa WhatsApp ativa. O lembrete permanece programado.";
            lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var jaEnfileirado = await dbContext.MensagensWhatsApp.AnyAsync(
            item => item.ContaPsicologoId == lembrete.ContaPsicologoId && item.LembreteId == lembrete.Id, cancellationToken);
        if (jaEnfileirado) return;
        dbContext.MensagensWhatsApp.Add(new MensagemWhatsApp
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = lembrete.ContaPsicologoId,
            ConversaWhatsAppId = conversa.Id,
            LembreteId = lembrete.Id,
            Direcao = DirecaoMensagem.Saida,
            TipoMensagem = TipoMensagemWhatsApp.Texto,
            Conteudo = protecaoDados.Proteger(lembrete.Mensagem ?? string.Empty),
            Status = StatusMensagemWhatsApp.Pendente,
            ErroEnvio = "Aguardando confirmação de entrega pelo Bridge.",
            CriadoEm = DateTimeOffset.UtcNow
        });
        conversa.UltimaMensagemEm = DateTimeOffset.UtcNow;
        conversa.AtualizadoEm = DateTimeOffset.UtcNow;

        lembrete.ErroEnvio = "Aguardando envio pelo WhatsApp.";
        lembrete.AtualizadoEm = DateTimeOffset.UtcNow;
        auditoria.Registrar(
            lembrete.ContaPsicologoId,
            usuarioId,
            "LembreteEnfileiradoWhatsApp",
            nameof(Lembrete),
            lembrete.Id,
            "Lembrete registrado para entrega via Bridge WhatsApp.",
            null,
            new { lembrete.Status, lembrete.EnviadoEm, conversa.Id },
            ip,
            userAgent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task NotificarFalhaLembreteAsync(Lembrete lembrete, CancellationToken cancellationToken)
    {
        var equipe = await dbContext.Usuarios.AsNoTracking()
            .Include(usuario => usuario.PerfilAcesso)
            .Where(usuario =>
                usuario.ContaPsicologoId == lembrete.ContaPsicologoId &&
                usuario.Status == StatusUsuario.Ativo)
            .ToArrayAsync(cancellationToken);
        var destinatarios = equipe
            .Where(usuario => PermissoesEfetivas.Tem(usuario, PermissaoAcesso.Mensagens))
            .Select(usuario => usuario.Id)
            .ToArray();

        foreach (var destinatarioId in destinatarios)
        {
            await notificacoes.CriarAsync(
                lembrete.ContaPsicologoId,
                destinatarioId,
                "Falha de lembrete",
                lembrete.ErroEnvio ?? "Um lembrete administrativo não foi enviado.",
                "lembrete_falha",
                cancellationToken);
        }
    }

    private static void ValidarCanal(CanalComunicacao canal)
    {
        if (!CanaisOperacionais.Contains(canal))
            throw new RegraNegocioException("Canal não disponível nesta etapa. Utilize E-mail ou WhatsApp.");
    }

    private static void ValidarPeriodo(FiltroLembreteRequest filtro)
    {
        if (filtro.PeriodoInicio is not null &&
            filtro.PeriodoFim is not null &&
            filtro.PeriodoFim < filtro.PeriodoInicio)
        {
            throw new RegraNegocioException("O fim do período deve ser posterior ao início.");
        }
    }

    private static LembreteResponse Mapear(Lembrete item) => new(
        item.Id,
        item.ConsultaId,
        item.Consulta.InicioEm,
        item.PacienteId,
        item.Paciente.NomeCompleto,
        item.ModeloMensagemId,
        item.ModeloMensagem?.Nome,
        item.Canal,
        item.Tipo,
        item.Status,
        item.AgendadoPara,
        item.EnviadoEm,
        item.Mensagem,
        item.Assunto,
        item.ErroEnvio,
        item.CriadoEm,
        item.AtualizadoEm);
}

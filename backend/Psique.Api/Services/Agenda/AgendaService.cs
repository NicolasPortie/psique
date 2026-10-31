using Psique.Api.Data;
using Psique.Api.DTOs.Agenda;
using Psique.Api.Models;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Microsoft.EntityFrameworkCore;

namespace Psique.Api.Services.Agenda;

public sealed class AgendaService(
    AppDbContext dbContext,
    IAutorizacaoContaService autorizacao,
    IAuditoriaService auditoria) : IAgendaService
{
    public async Task<ConfiguracaoAtendimentoResponse> ObterConfiguracaoAsync(Guid usuarioId, Guid contaId, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        var configuracao = await ObterConfiguracaoAsync(contaId, true, ct);
        return Mapear(configuracao);
    }

    public async Task<ConfiguracaoAtendimentoResponse> AtualizarConfiguracaoAsync(Guid usuarioId, Guid contaId, AtualizarConfiguracaoAtendimentoRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        var configuracao = await ObterConfiguracaoAsync(contaId, true, ct);
        var antes = Mapear(configuracao);
        FusoHorarioConta.Resolver(request.FusoHorario);
        configuracao.FusoHorario = request.FusoHorario.Trim();
        configuracao.DuracaoPadraoMinutos = request.DuracaoPadraoMinutos;
        configuracao.ConfirmacaoAutomaticaConsulta = request.ConfirmacaoAutomaticaConsulta;
        configuracao.AntecedenciaMinimaAgendamentoHoras = request.AntecedenciaMinimaAgendamentoHoras;
        configuracao.AntecedenciaCancelamentoHoras = request.AntecedenciaCancelamentoHoras;
        configuracao.AntecedenciaRemarcacaoHoras = request.AntecedenciaRemarcacaoHoras;
        configuracao.ExigeJustificativaCancelamento = request.ExigeJustificativaCancelamento;
        ValidarCanalLembrete(request.CanalLembretePadrao);
        if (request.AntecedenciaLembreteHoras < 0 || request.AntecedenciaLembreteHoras > 720)
            throw new RegraNegocioException("A antecedência do lembrete deve estar entre 0 e 720 horas.");
        configuracao.LembretesAutomaticosAtivos = request.LembretesAutomaticosAtivos;
        configuracao.ProgramarLembreteAoCriarConsulta = request.ProgramarLembreteAoCriarConsulta;
        configuracao.ProgramarLembreteAoConfirmar = request.ProgramarLembreteAoConfirmar;
        configuracao.AntecedenciaLembreteHoras = request.AntecedenciaLembreteHoras;
        configuracao.CanalLembretePadrao = request.CanalLembretePadrao;
        configuracao.CobrarAoRealizar = request.CobrarAoRealizar;
        configuracao.CobrarFalta = request.CobrarFalta;
        configuracao.AtualizadoEm = DateTimeOffset.UtcNow;
        var depois = Mapear(configuracao);
        auditoria.Registrar(contaId, usuarioId, "ConfiguracaoAtendimentoAtualizada", nameof(ConfiguracaoAtendimento), configuracao.Id, "Configurações de atendimento atualizadas.", antes, depois, ip, userAgent);
        await dbContext.SaveChangesAsync(ct);
        return depois;
    }

    public async Task<IReadOnlyCollection<TipoAtendimentoResponse>> ListarTiposAsync(Guid usuarioId, Guid contaId, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        var tipos = await dbContext.TiposAtendimento.AsNoTracking().Where(x => x.ContaPsicologoId == contaId)
            .OrderBy(x => x.Nome).ToArrayAsync(ct);
        return tipos.Select(Mapear).ToArray();
    }

    public async Task<TipoAtendimentoResponse> SalvarTipoAsync(Guid usuarioId, Guid contaId, Guid? id, SalvarTipoAtendimentoRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        var nome = request.Nome.Trim();
        var duplicado = await dbContext.TiposAtendimento.AnyAsync(x => x.ContaPsicologoId == contaId && x.Id != id && x.Nome == nome, ct);
        if (duplicado) throw new RegraNegocioException("Já existe um tipo de atendimento com este nome.");
        var tipo = id is null ? new TipoAtendimento { Id = Guid.NewGuid(), ContaPsicologoId = contaId } : await ObterTipoAsync(contaId, id.Value, ct);
        var antes = id is null ? null : Mapear(tipo);
        tipo.Nome = nome; tipo.Descricao = Limpar(request.Descricao); tipo.DuracaoMinutos = request.DuracaoMinutos; tipo.ValorPadrao = request.ValorPadrao; tipo.ModalidadePadrao = request.ModalidadePadrao; tipo.Ativo = request.Ativo; tipo.AtualizadoEm = DateTimeOffset.UtcNow;
        if (id is null) dbContext.TiposAtendimento.Add(tipo);
        var depois = Mapear(tipo);
        auditoria.Registrar(contaId, usuarioId, id is null ? "TipoAtendimentoCriado" : "TipoAtendimentoAtualizado", nameof(TipoAtendimento), tipo.Id, $"Tipo de atendimento {tipo.Nome} salvo.", antes, depois, ip, userAgent);
        await dbContext.SaveChangesAsync(ct);
        return depois;
    }

    /// <summary>
    /// Inícios livres ("HH:mm", relógio do consultório) para uma consulta da duração pedida no dia: dentro dos
    /// horários de atendimento, fora de bloqueios e consultas, respeitando a antecedência mínima. Passo de 30 minutos.
    /// </summary>
    public async Task<IReadOnlyCollection<string>> ListarHorariosLivresAsync(Guid usuarioId, Guid contaId, DateOnly data, int duracaoMinutos, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        if (duracaoMinutos is < 10 or > 480) throw new RegraNegocioException("A duração deve estar entre 10 e 480 minutos.");
        var configuracao = await ObterConfiguracaoAsync(contaId, false, ct);
        var fuso = FusoHorarioConta.Resolver(configuracao.FusoHorario);
        DateTimeOffset ParaInstante(DateTime local) => new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), fuso.GetUtcOffset(local));

        var inicioDia = ParaInstante(data.ToDateTime(TimeOnly.MinValue));
        var fimDia = ParaInstante(data.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var diaSemana = (DiaSemana)(int)data.DayOfWeek;
        var temGrade = await dbContext.HorariosDisponiveis.AnyAsync(x => x.ContaPsicologoId == contaId && x.Ativo, ct);
        var faixas = temGrade
            ? await dbContext.HorariosDisponiveis.AsNoTracking()
                .Where(x => x.ContaPsicologoId == contaId && x.Ativo && x.DiaSemana == diaSemana)
                .OrderBy(x => x.HoraInicio)
                .Select(x => new { x.HoraInicio, x.HoraFim })
                .ToListAsync(ct)
            : [new { HoraInicio = new TimeOnly(7, 0), HoraFim = new TimeOnly(21, 0) }];

        var ocupados = await dbContext.Consultas.AsNoTracking()
            .Where(x => x.ContaPsicologoId == contaId && x.Status != StatusConsulta.Cancelada && x.Status != StatusConsulta.Remarcada && x.InicioEm < fimDia && x.FimEm > inicioDia)
            .Select(x => new { x.InicioEm, x.FimEm })
            .ToListAsync(ct);
        ocupados.AddRange(await dbContext.BloqueiosAgenda.AsNoTracking()
            .Where(x => x.ContaPsicologoId == contaId && x.InicioEm < fimDia && x.FimEm > inicioDia)
            .Select(x => new { x.InicioEm, x.FimEm })
            .ToListAsync(ct));

        var minimo = DateTimeOffset.UtcNow.AddHours(configuracao.AntecedenciaMinimaAgendamentoHoras ?? 0);
        var duracao = TimeSpan.FromMinutes(duracaoMinutos);
        var livres = new List<string>();
        foreach (var faixa in faixas)
        {
            for (var hora = faixa.HoraInicio; hora.Add(duracao) <= faixa.HoraFim && hora >= faixa.HoraInicio; hora = hora.AddMinutes(30))
            {
                var inicio = ParaInstante(data.ToDateTime(hora));
                var fim = inicio + duracao;
                if (inicio < minimo) continue;
                if (ocupados.Any(x => x.InicioEm < fim && x.FimEm > inicio)) continue;
                var rotulo = hora.ToString("HH:mm");
                if (!livres.Contains(rotulo)) livres.Add(rotulo);
                if (hora.AddMinutes(30) < hora) break;
            }
        }
        return livres;
    }

    public async Task<IReadOnlyCollection<HorarioDisponivelResponse>> ListarHorariosAsync(Guid usuarioId, Guid contaId, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        var horarios = await dbContext.HorariosDisponiveis.AsNoTracking().Where(x => x.ContaPsicologoId == contaId)
            .OrderBy(x => x.DiaSemana).ThenBy(x => x.HoraInicio).ToArrayAsync(ct);
        return horarios.Select(Mapear).ToArray();
    }

    public async Task<HorarioDisponivelResponse> SalvarHorarioAsync(Guid usuarioId, Guid contaId, Guid? id, SalvarHorarioDisponivelRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        if (request.HoraFim <= request.HoraInicio) throw new RegraNegocioException("O horário final deve ser posterior ao horário inicial.");
        var horario = id is null ? new HorarioDisponivel { Id = Guid.NewGuid(), ContaPsicologoId = contaId } : await ObterHorarioAsync(contaId, id.Value, ct);
        var antes = id is null ? null : Mapear(horario);
        horario.DiaSemana = request.DiaSemana; horario.HoraInicio = request.HoraInicio; horario.HoraFim = request.HoraFim; horario.Ativo = request.Ativo; horario.AtualizadoEm = DateTimeOffset.UtcNow;
        if (id is null) dbContext.HorariosDisponiveis.Add(horario);
        var depois = Mapear(horario);
        auditoria.Registrar(contaId, usuarioId, id is null ? "HorarioDisponivelCriado" : "HorarioDisponivelAtualizado", nameof(HorarioDisponivel), horario.Id, "Horário disponível salvo.", antes, depois, ip, userAgent);
        await dbContext.SaveChangesAsync(ct);
        return depois;
    }

    public async Task<IReadOnlyCollection<BloqueioAgendaResponse>> ListarBloqueiosAsync(Guid usuarioId, Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        var bloqueios = await dbContext.BloqueiosAgenda.AsNoTracking().Where(x => x.ContaPsicologoId == contaId && x.InicioEm < fim && x.FimEm > inicio)
            .OrderBy(x => x.InicioEm).ToArrayAsync(ct);
        return bloqueios.Select(Mapear).ToArray();
    }

    public async Task<BloqueioAgendaResponse> SalvarBloqueioAsync(Guid usuarioId, Guid contaId, Guid? id, SalvarBloqueioAgendaRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        if (request.FimEm <= request.InicioEm) throw new RegraNegocioException("O fim do bloqueio deve ser posterior ao início.");
        var bloqueio = id is null ? new BloqueioAgenda { Id = Guid.NewGuid(), ContaPsicologoId = contaId } : await ObterBloqueioAsync(contaId, id.Value, ct);
        var antes = id is null ? null : Mapear(bloqueio);
        bloqueio.Titulo = request.Titulo.Trim(); bloqueio.Descricao = Limpar(request.Descricao); bloqueio.InicioEm = request.InicioEm; bloqueio.FimEm = request.FimEm; bloqueio.DiaInteiro = request.DiaInteiro; bloqueio.AtualizadoEm = DateTimeOffset.UtcNow;
        if (id is null) dbContext.BloqueiosAgenda.Add(bloqueio);
        var depois = Mapear(bloqueio);
        auditoria.Registrar(contaId, usuarioId, id is null ? "BloqueioAgendaCriado" : "BloqueioAgendaAtualizado", nameof(BloqueioAgenda), bloqueio.Id, $"Bloqueio {bloqueio.Titulo} salvo.", antes, depois, ip, userAgent);
        await dbContext.SaveChangesAsync(ct);
        return depois;
    }

    public async Task RemoverBloqueioAsync(Guid usuarioId, Guid contaId, Guid id, string? ip, string? userAgent, CancellationToken ct)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, ct);
        var bloqueio = await ObterBloqueioAsync(contaId, id, ct);
        auditoria.Registrar(contaId, usuarioId, "BloqueioAgendaRemovido", nameof(BloqueioAgenda), id, $"Bloqueio {bloqueio.Titulo} removido.", Mapear(bloqueio), null, ip, userAgent);
        dbContext.BloqueiosAgenda.Remove(bloqueio);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<ConfiguracaoAtendimento> ObterConfiguracaoAsync(Guid contaId, bool rastrear, CancellationToken ct)
    {
        var consulta = dbContext.ConfiguracoesAtendimento.Where(x => x.ContaPsicologoId == contaId && x.Ativo);
        if (!rastrear) consulta = consulta.AsNoTracking();
        return await consulta.SingleOrDefaultAsync(ct) ?? throw new RegraNegocioException("Configurações de atendimento não encontradas.", StatusCodes.Status404NotFound);
    }
    private async Task<TipoAtendimento> ObterTipoAsync(Guid contaId, Guid id, CancellationToken ct) => await dbContext.TiposAtendimento.SingleOrDefaultAsync(x => x.ContaPsicologoId == contaId && x.Id == id, ct) ?? throw new RegraNegocioException("Tipo de atendimento não encontrado.", StatusCodes.Status404NotFound);
    private async Task<HorarioDisponivel> ObterHorarioAsync(Guid contaId, Guid id, CancellationToken ct) => await dbContext.HorariosDisponiveis.SingleOrDefaultAsync(x => x.ContaPsicologoId == contaId && x.Id == id, ct) ?? throw new RegraNegocioException("Horário disponível não encontrado.", StatusCodes.Status404NotFound);
    private async Task<BloqueioAgenda> ObterBloqueioAsync(Guid contaId, Guid id, CancellationToken ct) => await dbContext.BloqueiosAgenda.SingleOrDefaultAsync(x => x.ContaPsicologoId == contaId && x.Id == id, ct) ?? throw new RegraNegocioException("Bloqueio de agenda não encontrado.", StatusCodes.Status404NotFound);
    private static void ValidarCanalLembrete(CanalComunicacao canal)
    {
        if (canal != CanalComunicacao.Email)
            throw new RegraNegocioException("Os lembretes automáticos são enviados por e-mail.");
    }

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    private static ConfiguracaoAtendimentoResponse Mapear(ConfiguracaoAtendimento x) => new(
        x.FusoHorario,
        x.DuracaoPadraoMinutos,
        x.ConfirmacaoAutomaticaConsulta,
        x.AntecedenciaMinimaAgendamentoHoras,
        x.AntecedenciaCancelamentoHoras,
        x.AntecedenciaRemarcacaoHoras,
        x.ExigeJustificativaCancelamento,
        x.LembretesAutomaticosAtivos,
        x.ProgramarLembreteAoCriarConsulta,
        x.ProgramarLembreteAoConfirmar,
        x.AntecedenciaLembreteHoras,
        x.CanalLembretePadrao,
        x.CobrarAoRealizar,
        x.CobrarFalta);
    private static TipoAtendimentoResponse Mapear(TipoAtendimento x) => new(x.Id, x.Nome, x.Descricao, x.DuracaoMinutos, x.ValorPadrao, x.ModalidadePadrao, x.Ativo);
    private static HorarioDisponivelResponse Mapear(HorarioDisponivel x) => new(x.Id, x.DiaSemana, x.HoraInicio, x.HoraFim, x.Ativo);
    private static BloqueioAgendaResponse Mapear(BloqueioAgenda x) => new(x.Id, x.Titulo, x.Descricao, x.InicioEm, x.FimEm, x.DiaInteiro);
}

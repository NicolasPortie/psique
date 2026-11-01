using System.Globalization;
using Psique.Api.Data;
using Psique.Api.DTOs.Relatorios;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Microsoft.EntityFrameworkCore;

namespace Psique.Api.Services.Relatorios;

public sealed class RelatoriosAdministrativosService(
    AppDbContext dbContext,
    IAutorizacaoContaService autorizacao,
    IAuditoriaService auditoria,
    RelatorioHtmlRenderer htmlRenderer) : IRelatoriosAdministrativosService
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly IReadOnlyList<RelatorioCatalogoItemResponse> Catalogo =
    [
        new("sessoes-realizadas", "Sessões realizadas", "Sessões registradas no período (metadados administrativos).", false),
        new("consultas-agendadas", "Consultas agendadas", "Consultas ativas e programadas no período.", false),
        new("cancelamentos", "Cancelamentos", "Consultas canceladas no período.", false),
        new("faltas", "Faltas", "Consultas com não comparecimento no período.", false),
        new("taxa-comparecimento", "Taxa de comparecimento", "Indicadores de realização, cancelamento e faltas.", false),
        new("pacientes-status", "Pacientes ativos e inativos", "Distribuição administrativa de pacientes.", false),
        new("valores-recebidos", "Valores recebidos", "Pagamentos com status pago no período.", false),
        new("pagamentos-pendentes", "Pagamentos pendentes", "Pagamentos em aberto ou aguardando confirmação.", false),
        new("horarios-ocupados", "Horários mais ocupados", "Distribuição de consultas por dia e hora.", false),
        new("historico-atendimentos-paciente", "Histórico administrativo por paciente", "Atendimentos administrativos de um paciente.", true)
    ];

    public async Task<IReadOnlyCollection<RelatorioCatalogoItemResponse>> ListarCatalogoAsync(
        Guid usuarioId,
        Guid contaId,
        CancellationToken cancellationToken)
    {
        var usuario = await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        return Catalogo.Where(item => PodeGerar(usuario, item.Codigo)).ToArray();
    }

    public async Task<MesEmNumerosResponse> ObterMesEmNumerosAsync(
        Guid usuarioId,
        Guid contaId,
        int ano,
        int mes,
        CancellationToken cancellationToken)
    {
        var usuario = await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        if (ano is < 2000 or > 2100 || mes is < 1 or > 12)
            throw new RegraNegocioException("Informe um mês válido.");

        // O mês começa e termina no relógio do consultório, não no do servidor nem no do navegador.
        var fusoConta = await dbContext.ConfiguracoesAtendimento.AsNoTracking()
            .Where(item => item.ContaPsicologoId == contaId && item.Ativo)
            .Select(item => item.FusoHorario)
            .FirstOrDefaultAsync(cancellationToken);
        var fuso = Agenda.FusoHorarioConta.Resolver(fusoConta);
        DateTimeOffset NoFuso(DateTime data) => new(data, fuso.GetUtcOffset(data));
        var inicio = NoFuso(new DateTime(ano, mes, 1));
        var fim = NoFuso(new DateTime(ano, mes, 1).AddMonths(1));

        int? realizadas = null, pacientes = null, faltas = null, percentualFaltas = null, cancelamentos = null;
        if (PermissoesEfetivas.Tem(usuario, PermissaoAcesso.Agenda))
        {
            var doMes = await dbContext.Consultas.AsNoTracking()
                .Where(item => item.ContaPsicologoId == contaId && item.InicioEm >= inicio && item.InicioEm < fim)
                .Select(item => new { item.Status, item.PacienteId })
                .ToListAsync(cancellationToken);
            realizadas = doMes.Count(item => item.Status == StatusConsulta.Realizada);
            pacientes = doMes.Where(item => item.Status == StatusConsulta.Realizada).Select(item => item.PacienteId).Distinct().Count();
            faltas = doMes.Count(item => item.Status == StatusConsulta.NaoCompareceu);
            cancelamentos = doMes.Count(item => item.Status == StatusConsulta.Cancelada);
            var encerradas = realizadas.Value + faltas.Value;
            percentualFaltas = encerradas > 0 ? (int)Math.Round(faltas.Value * 100m / encerradas, MidpointRounding.AwayFromZero) : null;
        }

        decimal? recebido = null, aReceber = null, vencido = null;
        if (PermissoesEfetivas.Tem(usuario, PermissaoAcesso.Financeiro))
        {
            var agora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, fuso);
            var inicioHoje = NoFuso(agora.Date);
            // Mesmo recorte da tela de Financeiro: a data de referência é o vencimento, o pagamento ou a criação.
            var pagamentos = await dbContext.Pagamentos.AsNoTracking()
                .Where(item => item.ContaPsicologoId == contaId &&
                               (item.VencimentoEm ?? item.PagoEm ?? item.CriadoEm) >= inicio &&
                               (item.VencimentoEm ?? item.PagoEm ?? item.CriadoEm) < fim)
                .Select(item => new { item.Status, item.Valor, item.VencimentoEm })
                .ToListAsync(cancellationToken);
            var emAberto = pagamentos.Where(item => item.Status is StatusPagamento.Pendente or StatusPagamento.AguardandoConfirmacao).ToList();
            recebido = pagamentos.Where(item => item.Status == StatusPagamento.Pago).Sum(item => item.Valor);
            aReceber = emAberto.Sum(item => item.Valor);
            vencido = emAberto.Where(item => item.VencimentoEm < inicioHoje).Sum(item => item.Valor);
        }

        return new MesEmNumerosResponse(ano, mes, realizadas, pacientes, faltas, percentualFaltas, cancelamentos, recebido, aReceber, vencido);
    }

    public async Task<RelatorioGeradoResponse> GerarAsync(
        Guid usuarioId,
        Guid contaId,
        string codigo,
        GerarRelatorioRequest request,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var usuario = await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        ValidarPeriodo(request);

        var item = Catalogo.FirstOrDefault(c => c.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase))
            ?? throw new RegraNegocioException("Relatório não encontrado.", StatusCodes.Status404NotFound);

        if (!PodeGerar(usuario, item.Codigo))
            throw new RegraNegocioException("Você não tem permissão para acessar os dados deste relatório.", StatusCodes.Status403Forbidden);

        if (item.ExigePaciente && request.PacienteId is null)
            throw new RegraNegocioException("Este relatório exige a seleção de um paciente.");

        var inicio = request.PeriodoInicio.ToUniversalTime();
        var fim = request.PeriodoFim.ToUniversalTime();
        var fusoConta = await dbContext.ConfiguracoesAtendimento.AsNoTracking()
            .Where(x => x.ContaPsicologoId == contaId && x.Ativo)
            .Select(x => x.FusoHorario).FirstOrDefaultAsync(cancellationToken);
        var fuso = Agenda.FusoHorarioConta.Resolver(fusoConta);
        var contexto = await MontarContextoAsync(contaId, cancellationToken);

        var (secaoTitulo, filtrosExtras, indicadores, conteudo) = codigo.ToLowerInvariant() switch
        {
            "sessoes-realizadas" => await SessoesRealizadasAsync(contaId, inicio, fim, fuso, cancellationToken),
            "consultas-agendadas" => await ConsultasAgendadasAsync(contaId, inicio, fim, fuso, cancellationToken),
            "cancelamentos" => await CancelamentosAsync(contaId, inicio, fim, fuso, cancellationToken),
            "faltas" => await FaltasAsync(contaId, inicio, fim, fuso, cancellationToken),
            "taxa-comparecimento" => await TaxaComparecimentoAsync(contaId, inicio, fim, cancellationToken),
            "pacientes-status" => await PacientesStatusAsync(contaId, fuso, cancellationToken),
            "valores-recebidos" => await ValoresRecebidosAsync(contaId, inicio, fim, fuso, cancellationToken),
            "pagamentos-pendentes" => await PagamentosPendentesAsync(contaId, inicio, fim, fuso, cancellationToken),
            "horarios-ocupados" => await HorariosOcupadosAsync(contaId, inicio, fim, fuso, cancellationToken),
            "historico-atendimentos-paciente" => await HistoricoPacienteAsync(contaId, request.PacienteId!.Value, inicio, fim, fuso, cancellationToken),
            _ => throw new RegraNegocioException("Relatório não encontrado.", StatusCodes.Status404NotFound)
        };

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["titulo"] = RelatorioHtmlRenderer.Codificar(item.Nome),
            ["nome_consultorio"] = RelatorioHtmlRenderer.Codificar(contexto.NomeConta),
            ["nome_profissional"] = RelatorioHtmlRenderer.Codificar(contexto.NomeProfissional),
            ["gerado_em"] = RelatorioHtmlRenderer.Codificar(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, fuso).ToString("dd/MM/yyyy HH:mm", Cultura)),
            ["conta_id_curto"] = RelatorioHtmlRenderer.Codificar(contaId.ToString("N")[..8]),
            ["periodo"] = RelatorioHtmlRenderer.Codificar($"{TimeZoneInfo.ConvertTime(inicio, fuso):dd/MM/yyyy} a {TimeZoneInfo.ConvertTime(fim, fuso):dd/MM/yyyy}"),
            ["filtros_extras"] = filtrosExtras,
            ["indicadores"] = indicadores,
            ["secao_titulo"] = RelatorioHtmlRenderer.Codificar(secaoTitulo),
            ["conteudo"] = conteudo
        };

        var html = htmlRenderer.RenderLayout(tokens);

        auditoria.Registrar(
            contaId,
            usuarioId,
            "RelatorioAdministrativoGerado",
            "RelatorioAdministrativo",
            null,
            $"Relatório administrativo gerado: {item.Nome}.",
            null,
            new { item.Codigo, inicio, fim, request.PacienteId },
            ip,
            userAgent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RelatorioGeradoResponse(item.Codigo, item.Nome, html, DateTimeOffset.UtcNow, inicio, fim);
    }

    private async Task<(string, string, string, string)> SessoesRealizadasAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken ct)
    {
        var itens = await dbContext.RegistrosSessao.AsNoTracking()
            .Include(x => x.Paciente)
            .Include(x => x.TipoAtendimento)
            .Where(x => x.ContaPsicologoId == contaId && x.DataSessao >= inicio && x.DataSessao <= fim)
            .OrderBy(x => x.DataSessao)
            .Select(x => new
            {
                x.DataSessao,
                Paciente = x.Paciente.NomeCompleto,
                Tipo = x.TipoAtendimento.Nome,
                x.Status
            })
            .ToListAsync(ct);

        var linhas = itens.Select(x => (IReadOnlyList<string>)
        [
            TimeZoneInfo.ConvertTime(x.DataSessao, fuso).ToString("dd/MM/yyyy HH:mm", Cultura),
            x.Paciente,
            x.Tipo,
            x.Status.ToString()
        ]).ToList();

        return (
            "Detalhamento de sessões",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(("Sessões", itens.Count.ToString(Cultura))),
            RelatorioHtmlRenderer.Tabela(["Data", "Paciente", "Tipo", "Status"], linhas));
    }

    private async Task<(string, string, string, string)> ConsultasAgendadasAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken ct)
    {
        var statusAtivos = new[]
        {
            StatusConsulta.AguardandoConfirmacao,
            StatusConsulta.Agendada,
            StatusConsulta.Confirmada
        };

        var itens = await dbContext.Consultas.AsNoTracking()
            .Include(x => x.Paciente)
            .Include(x => x.TipoAtendimento)
            .Where(x => x.ContaPsicologoId == contaId &&
                        x.InicioEm >= inicio &&
                        x.InicioEm <= fim &&
                        statusAtivos.Contains(x.Status))
            .OrderBy(x => x.InicioEm)
            .ToListAsync(ct);

        var linhas = itens.Select(x => (IReadOnlyList<string>)
        [
            TimeZoneInfo.ConvertTime(x.InicioEm, fuso).ToString("dd/MM/yyyy HH:mm", Cultura),
            x.Paciente.NomeCompleto,
            x.TipoAtendimento.Nome,
            RotuloStatusConsulta(x.Status),
            RotuloModalidade(x.Modalidade)
        ]).ToList();

        return (
            "Consultas no período",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(("Agendadas", itens.Count.ToString(Cultura))),
            RelatorioHtmlRenderer.Tabela(["Início", "Paciente", "Tipo", "Status", "Modalidade"], linhas));
    }

    private async Task<(string, string, string, string)> CancelamentosAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken ct)
    {
        var itens = await dbContext.Consultas.AsNoTracking()
            .Include(x => x.Paciente)
            .Include(x => x.TipoAtendimento)
            .Where(x => x.ContaPsicologoId == contaId &&
                        x.Status == StatusConsulta.Cancelada &&
                        x.InicioEm >= inicio &&
                        x.InicioEm <= fim)
            .OrderBy(x => x.InicioEm)
            .ToListAsync(ct);

        var linhas = itens.Select(x => (IReadOnlyList<string>)
        [
            TimeZoneInfo.ConvertTime(x.InicioEm, fuso).ToString("dd/MM/yyyy HH:mm", Cultura),
            x.Paciente.NomeCompleto,
            x.TipoAtendimento.Nome,
            x.JustificativaCancelamento ?? "—"
        ]).ToList();

        return (
            "Consultas canceladas",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(("Cancelamentos", itens.Count.ToString(Cultura))),
            RelatorioHtmlRenderer.Tabela(["Data prevista", "Paciente", "Tipo", "Justificativa"], linhas));
    }

    private async Task<(string, string, string, string)> FaltasAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken ct)
    {
        var itens = await dbContext.Consultas.AsNoTracking()
            .Include(x => x.Paciente)
            .Include(x => x.TipoAtendimento)
            .Where(x => x.ContaPsicologoId == contaId &&
                        x.Status == StatusConsulta.NaoCompareceu &&
                        x.InicioEm >= inicio &&
                        x.InicioEm <= fim)
            .OrderBy(x => x.InicioEm)
            .ToListAsync(ct);

        var linhas = itens.Select(x => (IReadOnlyList<string>)
        [
            TimeZoneInfo.ConvertTime(x.InicioEm, fuso).ToString("dd/MM/yyyy HH:mm", Cultura),
            x.Paciente.NomeCompleto,
            x.TipoAtendimento.Nome,
            RotuloModalidade(x.Modalidade)
        ]).ToList();

        return (
            "Não comparecimentos",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(("Faltas", itens.Count.ToString(Cultura))),
            RelatorioHtmlRenderer.Tabela(["Data", "Paciente", "Tipo", "Modalidade"], linhas));
    }

    private async Task<(string, string, string, string)> TaxaComparecimentoAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        var consultas = await dbContext.Consultas.AsNoTracking()
            .Where(x => x.ContaPsicologoId == contaId && x.InicioEm >= inicio && x.InicioEm <= fim)
            .Select(x => x.Status)
            .ToListAsync(ct);

        var realizadas = consultas.Count(x => x == StatusConsulta.Realizada);
        var canceladas = consultas.Count(x => x == StatusConsulta.Cancelada);
        var faltas = consultas.Count(x => x == StatusConsulta.NaoCompareceu);
        var baseComparecimento = realizadas + faltas;
        var taxa = baseComparecimento == 0
            ? "—"
            : ((decimal)realizadas / baseComparecimento).ToString("P1", Cultura);

        var linhas = new List<IReadOnlyList<string>>
        {
            new[] { "Realizadas", realizadas.ToString(Cultura) },
            new[] { "Canceladas", canceladas.ToString(Cultura) },
            new[] { "Faltas", faltas.ToString(Cultura) },
            new[] { "Total no período", consultas.Count.ToString(Cultura) },
            new[] { "Taxa de comparecimento", taxa }
        };

        return (
            "Resumo de comparecimento",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(
                ("Realizadas", realizadas.ToString(Cultura)),
                ("Canceladas", canceladas.ToString(Cultura)),
                ("Faltas", faltas.ToString(Cultura)),
                ("Taxa", taxa)),
            RelatorioHtmlRenderer.Tabela(["Indicador", "Valor"], linhas));
    }

    private async Task<(string, string, string, string)> PacientesStatusAsync(Guid contaId, TimeZoneInfo fuso, CancellationToken ct)
    {
        var itens = await dbContext.Pacientes.AsNoTracking()
            .Where(x => x.ContaPsicologoId == contaId)
            .Select(x => new { x.NomeCompleto, x.Status, x.CriadoEm, x.Email, x.Telefone })
            .ToListAsync(ct);
        // Nome cifrado no banco: ordena depois de decifrar.
        itens = itens.OrderBy(x => x.NomeCompleto, StringComparer.Create(Cultura, ignoreCase: true)).ToList();

        var ativos = itens.Count(x => x.Status == StatusPaciente.Ativo);
        var inativos = itens.Count(x => x.Status == StatusPaciente.Inativo);
        var arquivados = itens.Count(x => x.Status == StatusPaciente.Arquivado);

        var linhas = itens.Select(x => (IReadOnlyList<string>)
        [
            x.NomeCompleto,
            x.Status.ToString(),
            x.Email ?? "—",
            x.Telefone ?? "—",
            TimeZoneInfo.ConvertTime(x.CriadoEm, fuso).ToString("dd/MM/yyyy", Cultura)
        ]).ToList();

        return (
            "Cadastro de pacientes",
            RelatorioHtmlRenderer.LinhaFiltro("Escopo", "Toda a conta (independente do período)"),
            RelatorioHtmlRenderer.Indicadores(
                ("Ativos", ativos.ToString(Cultura)),
                ("Inativos", inativos.ToString(Cultura)),
                ("Arquivados", arquivados.ToString(Cultura)),
                ("Total", itens.Count.ToString(Cultura))),
            RelatorioHtmlRenderer.Tabela(["Paciente", "Status", "E-mail", "Telefone", "Cadastro"], linhas));
    }

    private async Task<(string, string, string, string)> ValoresRecebidosAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken ct)
    {
        var itens = await dbContext.Pagamentos.AsNoTracking()
            .Include(x => x.Paciente)
            .Where(x => x.ContaPsicologoId == contaId &&
                        x.Status == StatusPagamento.Pago &&
                        x.PagoEm != null &&
                        x.PagoEm >= inicio &&
                        x.PagoEm <= fim)
            .OrderBy(x => x.PagoEm)
            .ToListAsync(ct);

        var total = itens.Sum(x => x.Valor);
        var linhas = itens.Select(x => (IReadOnlyList<string>)
        [
            TimeZoneInfo.ConvertTime(x.PagoEm!.Value, fuso).ToString("dd/MM/yyyy", Cultura),
            x.Paciente.NomeCompleto,
            x.Valor.ToString("C", Cultura),
            x.FormaPagamento.ToString()
        ]).ToList();

        return (
            "Pagamentos recebidos",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(
                ("Recebimentos", itens.Count.ToString(Cultura)),
                ("Total", total.ToString("C", Cultura))),
            RelatorioHtmlRenderer.Tabela(["Pago em", "Paciente", "Valor", "Forma"], linhas));
    }

    private async Task<(string, string, string, string)> PagamentosPendentesAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken ct)
    {
        var statusPendentes = new[] { StatusPagamento.Pendente, StatusPagamento.AguardandoConfirmacao };
        var itens = await dbContext.Pagamentos.AsNoTracking()
            .Include(x => x.Paciente)
            .Where(x => x.ContaPsicologoId == contaId &&
                        statusPendentes.Contains(x.Status) &&
                        ((x.VencimentoEm != null && x.VencimentoEm >= inicio && x.VencimentoEm <= fim) ||
                         (x.VencimentoEm == null && x.CriadoEm >= inicio && x.CriadoEm <= fim)))
            .OrderBy(x => x.VencimentoEm ?? x.CriadoEm)
            .ToListAsync(ct);

        var total = itens.Sum(x => x.Valor);
        var linhas = itens.Select(x => (IReadOnlyList<string>)
        [
            x.Paciente.NomeCompleto,
            x.Valor.ToString("C", Cultura),
            x.Status.ToString(),
            x.VencimentoEm is { } vencimento ? TimeZoneInfo.ConvertTime(vencimento, fuso).ToString("dd/MM/yyyy", Cultura) : "Sem vencimento"
        ]).ToList();

        return (
            "Pagamentos em aberto",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(
                ("Pendentes", itens.Count.ToString(Cultura)),
                ("Valor em aberto", total.ToString("C", Cultura))),
            RelatorioHtmlRenderer.Tabela(["Paciente", "Valor", "Status", "Vencimento"], linhas));
    }

    private async Task<(string, string, string, string)> HorariosOcupadosAsync(Guid contaId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken ct)
    {
        var consultas = await dbContext.Consultas.AsNoTracking()
            .Where(x => x.ContaPsicologoId == contaId &&
                        x.InicioEm >= inicio &&
                        x.InicioEm <= fim &&
                        x.Status != StatusConsulta.Cancelada &&
                        x.Status != StatusConsulta.Remarcada)
            .Select(x => x.InicioEm)
            .ToListAsync(ct);

        var agrupado = consultas
            .GroupBy(x => new
            {
                Dia = TimeZoneInfo.ConvertTime(x, fuso).ToString("dddd", Cultura),
                Hora = TimeZoneInfo.ConvertTime(x, fuso).Hour
            })
            .Select(g => new
            {
                g.Key.Dia,
                Hora = $"{g.Key.Hora:00}:00",
                Quantidade = g.Count()
            })
            .OrderByDescending(x => x.Quantidade)
            .ThenBy(x => x.Dia)
            .ThenBy(x => x.Hora)
            .ToList();

        var linhas = agrupado.Select(x => (IReadOnlyList<string>)
        [
            Cultura.TextInfo.ToTitleCase(x.Dia),
            x.Hora,
            x.Quantidade.ToString(Cultura)
        ]).ToList();

        var topo = agrupado.FirstOrDefault();
        return (
            "Distribuição de horários",
            string.Empty,
            RelatorioHtmlRenderer.Indicadores(
                ("Consultas", consultas.Count.ToString(Cultura)),
                ("Horário de pico", topo is null ? "—" : $"{Cultura.TextInfo.ToTitleCase(topo.Dia)} {topo.Hora}")),
            RelatorioHtmlRenderer.Tabela(["Dia da semana", "Hora", "Consultas"], linhas));
    }

    private async Task<(string, string, string, string)> HistoricoPacienteAsync(
        Guid contaId,
        Guid pacienteId,
        DateTimeOffset inicio,
        DateTimeOffset fim,
        TimeZoneInfo fuso,
        CancellationToken ct)
    {
        var paciente = await dbContext.Pacientes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == pacienteId && x.ContaPsicologoId == contaId, ct)
            ?? throw new RegraNegocioException("Paciente não encontrado.", StatusCodes.Status404NotFound);

        var consultas = await dbContext.Consultas.AsNoTracking()
            .Include(x => x.TipoAtendimento)
            .Where(x => x.ContaPsicologoId == contaId &&
                        x.PacienteId == pacienteId &&
                        x.InicioEm >= inicio &&
                        x.InicioEm <= fim)
            .OrderBy(x => x.InicioEm)
            .ToListAsync(ct);

        var linhas = consultas.Select(x => (IReadOnlyList<string>)
        [
            TimeZoneInfo.ConvertTime(x.InicioEm, fuso).ToString("dd/MM/yyyy HH:mm", Cultura),
            x.TipoAtendimento.Nome,
            RotuloStatusConsulta(x.Status),
            RotuloModalidade(x.Modalidade)
        ]).ToList();

        return (
            "Histórico administrativo de atendimentos",
            RelatorioHtmlRenderer.LinhaFiltro("Paciente", paciente.NomeCompleto),
            RelatorioHtmlRenderer.Indicadores(("Atendimentos", consultas.Count.ToString(Cultura))),
            RelatorioHtmlRenderer.Tabela(["Data", "Tipo", "Status", "Modalidade"], linhas));
    }

    private async Task<(string NomeConta, string NomeProfissional)> MontarContextoAsync(Guid contaId, CancellationToken ct)
    {
        var conta = await dbContext.ContasPsicologo.AsNoTracking()
            .SingleAsync(x => x.Id == contaId, ct);
        var profissional = await dbContext.DadosProfissionaisPsicologo.AsNoTracking()
            .Where(x => x.ContaPsicologoId == contaId && x.Status == StatusUsuario.Ativo)
            .OrderBy(x => x.CriadoEm)
            .Select(x => x.NomeProfissional)
            .FirstOrDefaultAsync(ct);

        return (conta.NomeConta, string.IsNullOrWhiteSpace(profissional) ? "Profissional" : profissional);
    }

    private static bool PodeGerar(Models.Usuario usuario, string codigo)
    {
        var permissoes = PermissoesEfetivas.Para(usuario);
        if (!permissoes.Contains(PermissaoAcesso.Relatorios)) return false;
        return codigo switch
        {
            "valores-recebidos" or "pagamentos-pendentes" => permissoes.Contains(PermissaoAcesso.Financeiro),
            "pacientes-status" => permissoes.Contains(PermissaoAcesso.Pacientes),
            "historico-atendimentos-paciente" => permissoes.Contains(PermissaoAcesso.Pacientes) && permissoes.Contains(PermissaoAcesso.Agenda),
            _ => permissoes.Contains(PermissaoAcesso.Agenda)
        };
    }

    private static void ValidarPeriodo(GerarRelatorioRequest request)
    {
        if (request.PeriodoFim < request.PeriodoInicio)
            throw new RegraNegocioException("O fim do período deve ser posterior ao início.");
        if ((request.PeriodoFim - request.PeriodoInicio).TotalDays > 400)
            throw new RegraNegocioException("O período máximo do relatório é de 400 dias.");
    }

    private static string RotuloStatusConsulta(StatusConsulta status) => status switch
    {
        StatusConsulta.AguardandoConfirmacao => "Aguardando confirmação",
        StatusConsulta.NaoCompareceu => "Não compareceu",
        _ => status.ToString()
    };

    private static string RotuloModalidade(ModalidadeAtendimento modalidade) => modalidade switch
    {
        ModalidadeAtendimento.Hibrido => "Híbrido",
        _ => modalidade.ToString()
    };
}

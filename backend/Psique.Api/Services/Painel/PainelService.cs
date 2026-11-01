using System.Globalization;
using Psique.Api.Data;
using Psique.Api.DTOs.Painel;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Microsoft.EntityFrameworkCore;
using Psique.Api.Services.Agenda;

namespace Psique.Api.Services.Painel;

public sealed class PainelService(AppDbContext dbContext, TimeProvider? relogio = null) : IPainelService
{
    private static readonly CultureInfo CulturaBrasil = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<PainelResumoResponse> ObterResumoAsync(
        Guid usuarioId,
        Guid contaPsicologoId,
        CancellationToken cancellationToken)
    {
        var usuario = await dbContext.Usuarios
            .AsNoTracking()
            .Include(item => item.PerfilAcesso)
            .SingleOrDefaultAsync(
                item => item.Id == usuarioId &&
                        item.ContaPsicologoId == contaPsicologoId,
                cancellationToken);

        if (usuario is null)
        {
            throw new RegraNegocioException(
                "Sessão inválida ou expirada.",
                StatusCodes.Status401Unauthorized);
        }

        // O painel só mostra o que a pessoa pode abrir: cada bloco depende da permissão da página correspondente.
        var permissoes = PermissoesEfetivas.Para(usuario);
        var podeAgenda = permissoes.Contains(PermissaoAcesso.Agenda);
        var podePacientes = permissoes.Contains(PermissaoAcesso.Pacientes);
        var podeFinanceiro = permissoes.Contains(PermissaoAcesso.Financeiro);
        var podeMensagens = permissoes.Contains(PermissaoAcesso.Mensagens);
        var podeConversas = permissoes.Contains(PermissaoAcesso.Conversas);

        var fusoConta = await dbContext.ConfiguracoesAtendimento.AsNoTracking()
            .Where(item => item.ContaPsicologoId == contaPsicologoId && item.Ativo)
            .Select(item => item.FusoHorario).FirstOrDefaultAsync(cancellationToken);
        var fuso = FusoHorarioConta.Resolver(fusoConta);
        var agora = TimeZoneInfo.ConvertTime((relogio ?? TimeProvider.System).GetUtcNow(), fuso);
        DateTimeOffset NoFuso(DateTime data) => new(data, fuso.GetUtcOffset(data));
        var hoje = DateOnly.FromDateTime(agora.DateTime);
        var inicioHoje = NoFuso(hoje.ToDateTime(TimeOnly.MinValue));
        var fimHoje = NoFuso(agora.Date.AddDays(1));
        var inicioMes = NoFuso(new DateTime(agora.Year, agora.Month, 1));
        var fimMes = NoFuso(new DateTime(agora.Year, agora.Month, 1).AddMonths(1));
        var fimSemana = NoFuso(agora.Date.AddDays(5));

        var consultasHoje = await dbContext.Consultas
            .AsNoTracking()
            .Where(consulta =>
                podeAgenda &&
                consulta.ContaPsicologoId == contaPsicologoId &&
                consulta.InicioEm >= inicioHoje &&
                consulta.InicioEm < fimHoje &&
                consulta.Status != StatusConsulta.Cancelada &&
                consulta.Status != StatusConsulta.Remarcada)
            .OrderBy(consulta => consulta.InicioEm)
            .Select(consulta => new PainelConsultaHojeResponse(
                consulta.Id,
                consulta.Paciente.NomeCompleto,
                TimeZoneInfo.ConvertTime(consulta.InicioEm, fuso).ToString("HH:mm"),
                checked((int)Math.Round((consulta.FimEm - consulta.InicioEm).TotalMinutes)),
                consulta.TipoAtendimento.Nome,
                consulta.Modalidade.ToString(),
                consulta.Status.ToString(),
                consulta.Status == StatusConsulta.Realizada))
            .ToListAsync(cancellationToken);

        // "Ainda por vir": em aberto e sem ter terminado. Só o status não basta: à noite, a consulta da manhã
        // que ninguém marcou como realizada continuaria contando como futura.
        var consultasRestantesHoje = await dbContext.Consultas
            .AsNoTracking()
            .CountAsync(
                consulta =>
                    podeAgenda &&
                    consulta.ContaPsicologoId == contaPsicologoId &&
                    consulta.InicioEm >= inicioHoje &&
                    consulta.InicioEm < fimHoje &&
                    consulta.FimEm > agora &&
                    (consulta.Status == StatusConsulta.Agendada ||
                     consulta.Status == StatusConsulta.Confirmada ||
                     consulta.Status == StatusConsulta.AguardandoConfirmacao),
                cancellationToken);

        var pacientesAtivos = await dbContext.Pacientes
            .AsNoTracking()
            .CountAsync(
                paciente =>
                    podePacientes &&
                    paciente.ContaPsicologoId == contaPsicologoId &&
                    paciente.Status == StatusPaciente.Ativo,
                cancellationToken);

        var pagamentosPendentes = await dbContext.Pagamentos
            .AsNoTracking()
            .Where(pagamento =>
                podeFinanceiro &&
                pagamento.ContaPsicologoId == contaPsicologoId &&
                (pagamento.Status == StatusPagamento.Pendente ||
                 pagamento.Status == StatusPagamento.AguardandoConfirmacao))
            .SumAsync(pagamento => (decimal?)pagamento.Valor, cancellationToken) ?? 0;

        var consultasAguardando = await dbContext.Consultas
            .AsNoTracking()
            .CountAsync(
                consulta =>
                    podeAgenda &&
                    consulta.ContaPsicologoId == contaPsicologoId &&
                    consulta.Status == StatusConsulta.AguardandoConfirmacao,
                cancellationToken);

        var consultasMes = await dbContext.Consultas
            .AsNoTracking()
            .CountAsync(
                consulta =>
                    podeAgenda &&
                    consulta.ContaPsicologoId == contaPsicologoId &&
                    consulta.InicioEm >= inicioMes &&
                    consulta.InicioEm < fimMes &&
                    consulta.Status != StatusConsulta.Cancelada &&
                    consulta.Status != StatusConsulta.Remarcada,
                cancellationToken);

        var consultasRealizadasMes = await dbContext.Consultas
            .AsNoTracking()
            .CountAsync(
                consulta =>
                    podeAgenda &&
                    consulta.ContaPsicologoId == contaPsicologoId &&
                    consulta.InicioEm >= inicioMes &&
                    consulta.InicioEm < fimMes &&
                    consulta.Status == StatusConsulta.Realizada,
                cancellationToken);

        var lembretesFalhosHoje = await dbContext.Lembretes
            .AsNoTracking()
            .CountAsync(
                lembrete =>
                    podeMensagens &&
                    lembrete.ContaPsicologoId == contaPsicologoId &&
                    lembrete.Status == StatusLembrete.Falhou &&
                    lembrete.AgendadoPara >= inicioHoje &&
                    lembrete.AgendadoPara < fimHoje,
                cancellationToken);

        var conversasAbertas = await dbContext.ConversasWhatsApp
            .AsNoTracking()
            .CountAsync(
                conversa =>
                    podeConversas &&
                    conversa.ContaPsicologoId == contaPsicologoId &&
                    conversa.Status == StatusConversaWhatsApp.Aberta,
                cancellationToken);

        var conversasAtendimentoHumano = await dbContext.ConversasWhatsApp
            .AsNoTracking()
            .CountAsync(
                conversa =>
                    podeConversas &&
                    conversa.ContaPsicologoId == contaPsicologoId &&
                    conversa.AtendimentoHumanoNecessario,
                cancellationToken);

        var notificacoesNaoLidas = await dbContext.Notificacoes
            .AsNoTracking()
            .CountAsync(
                notificacao =>
                    notificacao.ContaPsicologoId == contaPsicologoId &&
                    notificacao.UsuarioId == usuarioId &&
                    !notificacao.Lida,
                cancellationToken);

        // Em aberto = ainda pode ser confirmada, realizada ou marcada como falta.
        var emAberto = new[] { StatusConsulta.AguardandoConfirmacao, StatusConsulta.Agendada, StatusConsulta.Confirmada };
        var proximas = await dbContext.Consultas
            .AsNoTracking()
            .Where(consulta =>
                podeAgenda &&
                consulta.ContaPsicologoId == contaPsicologoId &&
                consulta.FimEm > agora &&
                emAberto.Contains(consulta.Status))
            .OrderBy(consulta => consulta.InicioEm)
            .Take(3)
            .Select(consulta => new PainelConsultaResponse(
                consulta.Id,
                consulta.PacienteId,
                consulta.Paciente.NomeCompleto,
                consulta.InicioEm,
                consulta.FimEm,
                consulta.TipoAtendimento.Nome,
                consulta.Modalidade.ToString(),
                consulta.Status.ToString(),
                consulta.LinkOnline))
            .ToListAsync(cancellationToken);

        var semDesfecho = await dbContext.Consultas
            .AsNoTracking()
            .Where(consulta =>
                podeAgenda &&
                consulta.ContaPsicologoId == contaPsicologoId &&
                consulta.FimEm <= agora &&
                consulta.InicioEm >= inicioHoje.AddDays(-60) &&
                emAberto.Contains(consulta.Status))
            .OrderByDescending(consulta => consulta.InicioEm)
            .Take(20)
            .Select(consulta => new PainelConsultaResponse(
                consulta.Id,
                consulta.PacienteId,
                consulta.Paciente.NomeCompleto,
                consulta.InicioEm,
                consulta.FimEm,
                consulta.TipoAtendimento.Nome,
                consulta.Modalidade.ToString(),
                consulta.Status.ToString(),
                consulta.LinkOnline))
            .ToListAsync(cancellationToken);

        return new PainelResumoResponse(
            new PainelUsuarioResponse(
                usuario.NomeCompleto,
                ObterPrimeiroNome(usuario.NomeCompleto),
                usuario.Perfil.ToString()),
            new PainelIndicadoresResponse(
                consultasHoje.Count,
                consultasRestantesHoje,
                consultasAguardando,
                pacientesAtivos,
                pagamentosPendentes,
                lembretesFalhosHoje,
                conversasAbertas,
                conversasAtendimentoHumano,
                notificacoesNaoLidas,
                consultasMes == 0
                    ? 0
                    : Math.Clamp(
                        checked((int)Math.Round(consultasRealizadasMes * 100.0 / consultasMes)),
                        0,
                        100)),
            consultasHoje,
            await ObterVisaoSemanaAsync(
                contaPsicologoId,
                fimHoje,
                fimSemana,
                podeAgenda,
                fuso,
                hoje,
                cancellationToken),
            CriarPrioridades(
                consultasAguardando,
                pagamentosPendentes,
                lembretesFalhosHoje,
                conversasAtendimentoHumano,
                notificacoesNaoLidas,
                consultasHoje.Count),
            proximas,
            semDesfecho,
            await ObterAniversariantesAsync(contaPsicologoId, hoje, podePacientes, cancellationToken),
            usuario.Perfil is PerfilUsuario.Psicologo or PerfilUsuario.Administrador
                ? await ObterPrimeirosPassosAsync(contaPsicologoId, cancellationToken)
                : null);
    }

    private async Task<IReadOnlyCollection<PainelAniversarianteResponse>> ObterAniversariantesAsync(
        Guid contaPsicologoId,
        DateOnly hoje,
        bool podePacientes,
        CancellationToken cancellationToken)
    {
        if (!podePacientes) return [];
        var nascimentos = await dbContext.Pacientes
            .AsNoTracking()
            .Where(paciente =>
                paciente.ContaPsicologoId == contaPsicologoId &&
                paciente.Status == StatusPaciente.Ativo &&
                paciente.DataNascimento != null)
            .Select(paciente => new { paciente.Id, paciente.NomeCompleto, Nascimento = paciente.DataNascimento!.Value })
            .ToListAsync(cancellationToken);

        return nascimentos
            .Select(item =>
            {
                // 29/02 vira 28/02 nos anos que não são bissextos.
                var dia = Math.Min(item.Nascimento.Day, DateTime.DaysInMonth(hoje.Year, item.Nascimento.Month));
                var aniversario = new DateOnly(hoje.Year, item.Nascimento.Month, dia);
                if (aniversario < hoje) aniversario = aniversario.AddYears(1);
                return new PainelAniversarianteResponse(item.Id, item.NomeCompleto, aniversario, aniversario.Year - item.Nascimento.Year);
            })
            .Where(item => item.Data.DayNumber - hoje.DayNumber <= 7)
            .OrderBy(item => item.Data)
            .Take(8)
            .ToArray();
    }

    private async Task<PainelPrimeirosPassosResponse> ObterPrimeirosPassosAsync(Guid contaPsicologoId, CancellationToken cancellationToken) =>
        new(
            await dbContext.DadosProfissionaisPsicologo.AsNoTracking().AnyAsync(item => item.ContaPsicologoId == contaPsicologoId && item.Crp != "", cancellationToken),
            await dbContext.HorariosDisponiveis.AsNoTracking().AnyAsync(item => item.ContaPsicologoId == contaPsicologoId && item.Ativo, cancellationToken),
            await dbContext.TiposAtendimento.AsNoTracking().AnyAsync(item => item.ContaPsicologoId == contaPsicologoId && item.Ativo, cancellationToken),
            await dbContext.Pacientes.AsNoTracking().AnyAsync(item => item.ContaPsicologoId == contaPsicologoId, cancellationToken),
            await dbContext.IntegracoesWhatsApp.AsNoTracking().AnyAsync(item => item.ContaPsicologoId == contaPsicologoId && item.Status == StatusIntegracao.Ativa, cancellationToken));

    private async Task<IReadOnlyCollection<PainelDiaSemanaResponse>> ObterVisaoSemanaAsync(
        Guid contaPsicologoId,
        DateTimeOffset inicio,
        DateTimeOffset fim,
        bool podeAgenda,
        TimeZoneInfo fuso,
        DateOnly hoje,
        CancellationToken cancellationToken)
    {
        var consultas = await dbContext.Consultas
            .AsNoTracking()
            .Where(consulta =>
                podeAgenda &&
                consulta.ContaPsicologoId == contaPsicologoId &&
                consulta.InicioEm >= inicio &&
                consulta.InicioEm < fim &&
                consulta.Status != StatusConsulta.Cancelada &&
                consulta.Status != StatusConsulta.Remarcada)
            .Select(consulta => consulta.InicioEm)
            .ToListAsync(cancellationToken);

        var totaisPorDia = consultas
            .GroupBy(item => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item, fuso).Date))
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Count());

        return Enumerable.Range(0, 4)
            .Select(indice =>
            {
                var data = DateOnly.FromDateTime(inicio.AddDays(indice).Date);
                var total = totaisPorDia.GetValueOrDefault(data);
                return new PainelDiaSemanaResponse(data, ObterRotuloDia(data, hoje), total);
            })
            .ToArray();
    }

    private static IReadOnlyCollection<PainelPrioridadeResponse> CriarPrioridades(
        int consultasAguardando,
        decimal pagamentosPendentes,
        int lembretesFalhosHoje,
        int conversasAtendimentoHumano,
        int notificacoesNaoLidas,
        int consultasHoje)
    {
        var prioridades = new List<PainelPrioridadeResponse>();

        if (conversasAtendimentoHumano > 0)
        {
            prioridades.Add(new PainelPrioridadeResponse(
                "Conversas esperando uma pessoa",
                FormatarQuantidade(
                    conversasAtendimentoHumano,
                    "conversa do WhatsApp espera uma pessoa da equipe.",
                    "conversas do WhatsApp esperam uma pessoa da equipe."),
                "alerta",
                "/conversas",
                "Abrir conversas"));
        }

        if (lembretesFalhosHoje > 0)
        {
            prioridades.Add(new PainelPrioridadeResponse(
                "Lembretes com falha",
                FormatarQuantidade(
                    lembretesFalhosHoje,
                    "lembrete não foi enviado hoje.",
                    "lembretes não foram enviados hoje."),
                "erro",
                "/lembretes",
                "Revisar envios"));
        }

        if (consultasAguardando > 0)
        {
            prioridades.Add(new PainelPrioridadeResponse(
                "Confirmações pendentes",
                FormatarQuantidade(
                    consultasAguardando,
                    "consulta ainda aguarda confirmação.",
                    "consultas ainda aguardam confirmação."),
                "atencao",
                "/agenda",
                "Ver agenda"));
        }

        if (pagamentosPendentes > 0)
        {
            prioridades.Add(new PainelPrioridadeResponse(
                "Financeiro a acompanhar",
                $"{pagamentosPendentes.ToString("C", CulturaBrasil)} em cobranças ainda não recebidas.",
                "neutro",
                "/pagamentos",
                "Abrir financeiro"));
        }

        if (notificacoesNaoLidas > 0)
        {
            prioridades.Add(new PainelPrioridadeResponse(
                "Notificações não lidas",
                FormatarQuantidade(
                    notificacoesNaoLidas,
                    "notificação precisa de leitura.",
                    "notificações precisam de leitura."),
                "neutro",
                "/notificacoes",
                "Ler notificações"));
        }

        if (prioridades.Count == 0)
        {
            prioridades.Add(new PainelPrioridadeResponse(
                "Rotina administrativa em dia",
                consultasHoje == 0
                    ? "Sem pendências e sem consultas registradas para hoje."
                    : "Sem pendências críticas antes dos atendimentos de hoje.",
                "ok",
                "/agenda",
                "Abrir agenda"));
        }

        return prioridades.Take(4).ToArray();
    }

    private static string FormatarQuantidade(int quantidade, string singular, string plural) =>
        $"{quantidade} {(quantidade == 1 ? singular : plural)}";

    private static string ObterPrimeiroNome(string nomeCompleto) =>
        nomeCompleto
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(parte =>
                !string.Equals(parte, "Dr.", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parte, "Dra.", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parte, "Dr", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parte, "Dra", StringComparison.OrdinalIgnoreCase))
            ?? nomeCompleto;

    private static string ObterRotuloDia(DateOnly data, DateOnly hoje)
    {
        if (data == hoje.AddDays(1))
        {
            return "Amanhã";
        }

        return data.DayOfWeek switch
        {
            DayOfWeek.Monday => "Segunda",
            DayOfWeek.Tuesday => "Terça",
            DayOfWeek.Wednesday => "Quarta",
            DayOfWeek.Thursday => "Quinta",
            DayOfWeek.Friday => "Sexta",
            DayOfWeek.Saturday => "Sábado",
            _ => "Domingo"
        };
    }
}

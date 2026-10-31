namespace Psique.Api.DTOs.Painel;

public sealed record PainelResumoResponse(
    PainelUsuarioResponse Usuario,
    PainelIndicadoresResponse Indicadores,
    IReadOnlyCollection<PainelConsultaHojeResponse> ConsultasHoje,
    IReadOnlyCollection<PainelDiaSemanaResponse> VisaoSemana,
    IReadOnlyCollection<PainelPrioridadeResponse> Prioridades,
    IReadOnlyCollection<PainelConsultaResponse>? ProximasConsultas = null,
    IReadOnlyCollection<PainelConsultaResponse>? ConsultasSemDesfecho = null,
    IReadOnlyCollection<PainelAniversarianteResponse>? Aniversariantes = null,
    PainelPrimeirosPassosResponse? PrimeirosPassos = null);

/// <summary>Paciente ativo que faz aniversário nos próximos 7 dias (data deste ano e idade que vai completar).</summary>
public sealed record PainelAniversarianteResponse(Guid PacienteId, string Paciente, DateOnly Data, int Idade);

/// <summary>Configuração inicial do consultório; só para quem gere a conta.</summary>
public sealed record PainelPrimeirosPassosResponse(bool DadosProfissionais, bool HorariosAtendimento, bool TiposAtendimento, bool PrimeiroPaciente, bool WhatsApp);

/// <summary>Consulta resumida para "próxima sessão" e para as sessões que passaram sem desfecho.</summary>
public sealed record PainelConsultaResponse(
    Guid Id,
    Guid PacienteId,
    string Paciente,
    DateTimeOffset InicioEm,
    DateTimeOffset FimEm,
    string TipoAtendimento,
    string Modalidade,
    string Status,
    string? LinkOnline);

public sealed record PainelUsuarioResponse(
    string NomeCompleto,
    string PrimeiroNome,
    string Perfil);

public sealed record PainelIndicadoresResponse(
    int ConsultasHoje,
    int ConsultasRestantesHoje,
    int ConsultasAguardandoConfirmacao,
    int PacientesAtivos,
    decimal PagamentosPendentes,
    int LembretesFalhosHoje,
    int ConversasWhatsappAbertas,
    int ConversasWhatsappAtendimentoHumano,
    int NotificacoesNaoLidas,
    int ProgressoMensalPercentual);

public sealed record PainelConsultaHojeResponse(
    Guid Id,
    string Paciente,
    string HorarioInicio,
    int DuracaoMinutos,
    string TipoAtendimento,
    string Modalidade,
    string Status,
    bool PodeRegistrarSessao);

public sealed record PainelDiaSemanaResponse(
    DateOnly Data,
    string Rotulo,
    int Consultas);

public sealed record PainelPrioridadeResponse(
    string Titulo,
    string Mensagem,
    string Tom,
    string Rota,
    string Acao);

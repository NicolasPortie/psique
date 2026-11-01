using Psique.Api.Data.Cifra;
using Psique.Api.Shared.Enums;

namespace Psique.Api.Models;

public class Lembrete : EntidadeDaConta
{
    public Guid Id { get; set; }
    public Guid ConsultaId { get; set; }
    public Guid PacienteId { get; set; }
    public Guid? ModeloMensagemId { get; set; }
    public CanalComunicacao Canal { get; set; } = CanalComunicacao.Email;
    public TipoLembrete Tipo { get; set; }
    public StatusLembrete Status { get; set; } = StatusLembrete.Agendado;
    public DateTimeOffset AgendadoPara { get; set; }
    public DateTimeOffset? EnviadoEm { get; set; }
    public DateTimeOffset? EnvioEmailIniciadoEm { get; set; }
    [Cifrado]
    public string? Assunto { get; set; }
    [Cifrado]
    public string? Mensagem { get; set; }
    [Cifrado]
    public string? ErroEnvio { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AtualizadoEm { get; set; }

    public Consulta Consulta { get; set; } = null!;
    public Paciente Paciente { get; set; } = null!;
    public ModeloMensagem? ModeloMensagem { get; set; }
}

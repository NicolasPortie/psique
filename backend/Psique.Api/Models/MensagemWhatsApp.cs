using Psique.Api.Data.Cifra;
using Psique.Api.Shared.Enums;

namespace Psique.Api.Models;

public class MensagemWhatsApp : EntidadeDaConta
{
    public Guid Id { get; set; }
    public Guid ConversaWhatsAppId { get; set; }
    public string? MensagemExternaId { get; set; }
    public Guid? LembreteId { get; set; }
    public DateTimeOffset? EnvioIniciadoEm { get; set; }
    public DirecaoMensagem Direcao { get; set; }
    public TipoMensagemWhatsApp TipoMensagem { get; set; } = TipoMensagemWhatsApp.Texto;
    public string? Conteudo { get; set; }
    public StatusMensagemWhatsApp Status { get; set; } = StatusMensagemWhatsApp.Pendente;
    public DateTimeOffset? EnviadaEm { get; set; }
    public DateTimeOffset? RecebidaEm { get; set; }
    public DateTimeOffset? LidaEm { get; set; }
    [Cifrado]
    public string? ErroEnvio { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;

    public ConversaWhatsApp ConversaWhatsApp { get; set; } = null!;
}

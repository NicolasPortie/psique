using Psique.Api.Data.Cifra;
namespace Psique.Api.Models;

public class EventoBridgeWhatsApp : EntidadeDaConta
{
    public Guid Id { get; set; }
    public Guid IntegracaoWhatsAppId { get; set; }
    public string? EventoExternoId { get; set; }
    public string TipoEvento { get; set; } = string.Empty;
    public string? MensagemExternaId { get; set; }
    public Psique.Api.Shared.Enums.StatusMensagemWhatsApp? StatusMensagem { get; set; }
    public string? PayloadHash { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public bool Processado { get; set; }
    public DateTimeOffset? ProcessadoEm { get; set; }
    [Cifrado]
    public string? ErroProcessamento { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;

    public IntegracaoWhatsApp IntegracaoWhatsApp { get; set; } = null!;
}

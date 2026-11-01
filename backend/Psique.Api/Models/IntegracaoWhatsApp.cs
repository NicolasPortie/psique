using Psique.Api.Data.Cifra;
using Psique.Api.Shared.Enums;

namespace Psique.Api.Models;

public class IntegracaoWhatsApp : EntidadeDaConta
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Provedor { get; set; } = string.Empty;
    [Cifrado]
    public string? Instancia { get; set; }
    [Cifrado]
    public string UrlBaseBridge { get; set; } = string.Empty;
    public string TokenCriptografado { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    [Cifrado]
    public string NumeroWhatsApp { get; set; } = string.Empty;
    [Cifrado]
    public string? UltimoErro { get; set; }
    public DateTimeOffset? UltimaConexaoTestadaEm { get; set; }
    public StatusIntegracao Status { get; set; } = StatusIntegracao.Pendente;
    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AtualizadoEm { get; set; }

    public ICollection<ConversaWhatsApp> Conversas { get; set; } = [];
    public ICollection<EventoBridgeWhatsApp> Eventos { get; set; } = [];
}

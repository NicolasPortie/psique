using System.ComponentModel.DataAnnotations;
using Psique.Api.Shared.Enums;

namespace Psique.Api.DTOs.WhatsApp;

public sealed record IntegracaoWhatsAppResponse(
    Guid Id,
    string Nome,
    string Provedor,
    string UrlBaseBridge,
    string TokenMascarado,
    string NumeroWhatsApp,
    StatusIntegracao Status,
    bool Ativo,
    string? UltimoErro,
    DateTimeOffset? UltimaConexaoTestadaEm,
    DateTimeOffset CriadoEm,
    DateTimeOffset? AtualizadoEm,
    string? Instancia = null);

public sealed class SalvarIntegracaoWhatsAppRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Nome { get; init; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 2)]
    public string Provedor { get; init; } = "Evolution API";

    [StringLength(100)]
    public string? Instancia { get; init; }

    [Required, StringLength(500, MinimumLength = 8)]
    public string UrlBaseBridge { get; init; } = string.Empty;

    [StringLength(500, MinimumLength = 16)]
    public string? Token { get; init; }

    [Required, StringLength(30, MinimumLength = 8)]
    public string NumeroWhatsApp { get; init; } = string.Empty;

    public bool Ativo { get; init; } = true;
}

public sealed record TesteConexaoBridgeResponse(
    bool Sucesso,
    StatusIntegracao Status,
    string Mensagem,
    DateTimeOffset TestadoEm);

public sealed record EventoBridgeResumoResponse(
    Guid Id,
    string TipoEvento,
    bool Processado,
    string? ErroProcessamento,
    DateTimeOffset CriadoEm,
    DateTimeOffset? ProcessadoEm);

public sealed record ConversaWhatsAppListaResponse(
    Guid Id,
    Guid IntegracaoWhatsAppId,
    Guid? PacienteId,
    string? Paciente,
    string Telefone,
    string? NomeContato,
    StatusConversaWhatsApp Status,
    DateTimeOffset? UltimaMensagemEm,
    bool AtendimentoHumanoNecessario,
    string? MotivoAtendimentoHumano,
    string? PreviaUltimaMensagem,
    DateTimeOffset CriadoEm);

public sealed record MensagemWhatsAppResponse(
    Guid Id,
    DirecaoMensagem Direcao,
    TipoMensagemWhatsApp TipoMensagem,
    string? Conteudo,
    StatusMensagemWhatsApp Status,
    DateTimeOffset? EnviadaEm,
    DateTimeOffset? RecebidaEm,
    string? ErroEnvio,
    DateTimeOffset CriadoEm);

public sealed record ConversaWhatsAppDetalheResponse(
    Guid Id,
    Guid IntegracaoWhatsAppId,
    Guid? PacienteId,
    string? Paciente,
    string Telefone,
    string? NomeContato,
    StatusConversaWhatsApp Status,
    DateTimeOffset? UltimaMensagemEm,
    bool AtendimentoHumanoNecessario,
    string? MotivoAtendimentoHumano,
    DateTimeOffset CriadoEm,
    IReadOnlyCollection<MensagemWhatsAppResponse> Mensagens);

public sealed class FiltroConversaWhatsAppRequest
{
    public StatusConversaWhatsApp? Status { get; init; }
    public bool? AtendimentoHumanoNecessario { get; init; }
    public Guid? PacienteId { get; init; }
    public int Pagina { get; init; } = 1;
    public int TamanhoPagina { get; init; } = 20;
}

public sealed class VincularPacienteConversaRequest
{
    public Guid PacienteId { get; init; }
}

public sealed class AssumirAtendimentoHumanoRequest
{
    [StringLength(1000)]
    public string? Motivo { get; init; }
}

public sealed class EnviarMensagemConversaRequest
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Conteudo { get; init; } = string.Empty;
}

public sealed class ReceberEventoBridgeRequest
{
    [StringLength(180)]
    public string? EventoExternoId { get; init; }

    [Required, StringLength(120)]
    public string TipoEvento { get; init; } = string.Empty;

    [Required, StringLength(40)]
    public string Telefone { get; init; } = string.Empty;

    [StringLength(180)]
    public string? NomeContato { get; init; }

    [StringLength(4000)]
    public string? Conteudo { get; init; }

    [StringLength(180)]
    public string? MensagemExternaId { get; init; }
}

public sealed record ReceberEventoBridgeResponse(
    Guid EventoId,
    bool Processado,
    bool Duplicado,
    Guid? ConversaId,
    string Mensagem);

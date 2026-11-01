using System.Net.Http.Json;
using System.Text.Json;
using Psique.Api.Models;
using Psique.Api.Services.Seguranca;

namespace Psique.Api.Services.WhatsApp;

/// <summary>Contrato Evolution API v2. Aceite do provedor não significa leitura ou entrega ao aparelho.</summary>
public sealed class EvolutionApiClient(IHttpClientFactory fabrica, IProtecaoDadosService protecao)
{
    public static bool Compativel(IntegracaoWhatsApp integracao) =>
        string.Equals(integracao.Provedor.Trim(), "Evolution API", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> EstaConectadaAsync(IntegracaoWhatsApp integracao, CancellationToken ct)
    {
        using var resposta = await ExecutarAsync(integracao, HttpMethod.Get, "instance/connectionState", null, ct);
        if (!resposta.IsSuccessStatusCode) return false;
        using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return json.RootElement.ValueKind == JsonValueKind.Object &&
               json.RootElement.TryGetProperty("instance", out var instancia) && instancia.ValueKind == JsonValueKind.Object &&
               instancia.TryGetProperty("state", out var estado) && estado.ValueKind == JsonValueKind.String && estado.GetString() == "open";
    }

    public async Task<string> EnviarTextoAsync(IntegracaoWhatsApp integracao, string telefone, string texto, CancellationToken ct)
    {
        using var resposta = await ExecutarAsync(integracao, HttpMethod.Post, "message/sendText", new { number = telefone, text = texto }, ct);
        if (!resposta.IsSuccessStatusCode)
            throw new HttpRequestException($"O provedor recusou o envio (HTTP {(int)resposta.StatusCode}).");
        using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json.RootElement.ValueKind == JsonValueKind.Object &&
            json.RootElement.TryGetProperty("key", out var chave) && chave.ValueKind == JsonValueKind.Object && chave.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 and <= 180 } identificador)
            return identificador;
        throw new HttpRequestException("O provedor não confirmou o identificador da mensagem.");
    }

    private async Task<HttpResponseMessage> ExecutarAsync(IntegracaoWhatsApp integracao, HttpMethod metodo, string caminho, object? corpo, CancellationToken ct)
    {
        if (!Compativel(integracao) || string.IsNullOrWhiteSpace(integracao.Instancia))
            throw new HttpRequestException("Configure a Evolution API e o nome da instância antes de enviar.");
        using var pedido = new HttpRequestMessage(metodo, $"{integracao.UrlBaseBridge.TrimEnd('/')}/{caminho}/{Uri.EscapeDataString(integracao.Instancia)}");
        pedido.Headers.Add("apikey", protecao.Desproteger(integracao.TokenCriptografado));
        if (corpo is not null) pedido.Content = JsonContent.Create(corpo);
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(8));
        using var cliente = fabrica.CreateClient("BridgeWhatsApp");
        return await cliente.SendAsync(pedido, limite.Token);
    }
}

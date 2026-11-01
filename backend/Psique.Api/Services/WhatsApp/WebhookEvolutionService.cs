using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Psique.Api.Data;
using Psique.Api.DTOs.WhatsApp;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;

namespace Psique.Api.Services.WhatsApp;

public sealed class WebhookEvolutionService(AppDbContext db, IProtecaoDadosService protecao, IBridgeWhatsAppService bridge)
{
    public async Task ReceberAsync(JsonElement corpo, string? tokenHeader, string? ip, string? agente, CancellationToken ct)
    {
        var token = tokenHeader ?? Texto(corpo, "apikey") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token)) throw new RegraNegocioException("Chave de integração obrigatória.", 401);
        var hash = protecao.HashToken(token);
        var integracao = await db.IntegracoesWhatsApp.Include(i => i.ContaPsicologo)
            .SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (integracao is null) throw new RegraNegocioException("Chave de integração inválida.", 401);
        if (!integracao.Ativo || integracao.ContaPsicologo.Status != StatusContaPsicologo.Ativa)
            throw new RegraNegocioException("Integração indisponível.", 403);
        if (!EvolutionApiClient.Compativel(integracao) || Texto(corpo, "instance") != integracao.Instancia)
            throw new RegraNegocioException("Instância de integração inválida.", 403);
        var evento = Texto(corpo, "event");
        if (!corpo.TryGetProperty("data", out var dados)) throw new RegraNegocioException("Evento sem dados.");
        if (evento == "connection.update")
        {
            integracao.Status = Texto(dados, "state") == "open" ? StatusIntegracao.Ativa : StatusIntegracao.Pendente;
            integracao.AtualizadoEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        if (evento is not ("messages.upsert" or "messages.update")) return;
        var itens = dados.ValueKind == JsonValueKind.Array ? dados.EnumerateArray().ToArray() : [dados];
        foreach (var dado in itens)
        {
            var chave = dado.TryGetProperty("key", out var k) ? k : dado;
            var jid = Texto(chave, "remoteJid") ?? Texto(dado, "remoteJid");
            if (jid is null || !jid.EndsWith("@s.whatsapp.net", StringComparison.Ordinal)) continue;
            var numero = jid.Split('@')[0];
            if (numero.Length is < 10 or > 15 || !numero.All(char.IsAsciiDigit)) continue;
            var id = Texto(chave, "id") ?? Texto(dado, "keyId");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 120) continue;
            string? tipo;
            string? texto = null;
            if (evento == "messages.upsert")
            {
                if (chave.TryGetProperty("fromMe", out var proprio) && proprio.ValueKind == JsonValueKind.True) continue;
                if (!dado.TryGetProperty("message", out var mensagem)) continue;
                texto = Texto(mensagem, "conversation");
                if (texto is null && mensagem.TryGetProperty("extendedTextMessage", out var estendida)) texto = Texto(estendida, "text");
                if (string.IsNullOrWhiteSpace(texto)) continue;
                if (texto.Length > 4000) texto = texto[..4000];
                tipo = "mensagem.recebida";
            }
            else
            {
                var estado = dado.TryGetProperty("status", out var status) ? status.ToString() : null;
                tipo = estado switch
                {
                    "3" or "DELIVERY_ACK" => "mensagem.entregue",
                    "4" or "5" or "READ" or "PLAYED" => "mensagem.lida",
                    "0" or "ERROR" => "mensagem.falhou",
                    _ => null
                };
                if (tipo is null) continue;
            }
            await bridge.ReceberEventoAsync(token, new ReceberEventoBridgeRequest
            {
                EventoExternoId = $"evo:{id}:{tipo}", TipoEvento = tipo, Telefone = numero,
                MensagemExternaId = id, NomeContato = Texto(dado, "pushName") is { } nome ? nome[..Math.Min(nome.Length, 180)] : null,
                Conteudo = texto
            }, ip, agente, ct);
        }
    }

    private static string? Texto(JsonElement elemento, string nome) =>
        elemento.ValueKind == JsonValueKind.Object && elemento.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString() : null;
}

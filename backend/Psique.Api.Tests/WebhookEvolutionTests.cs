using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Psique.Api.Data;
using Psique.Api.DTOs.WhatsApp;
using Psique.Api.Models;
using Psique.Api.Services.WhatsApp;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;

namespace Psique.Api.Tests;

public sealed class WebhookEvolutionTests
{
    [Theory]
    [InlineData("outra", "chave", 403)]
    [InlineData("consultorio", "incorreta", 401)]
    public async Task Evento_RecusaInstanciaOuChaveDeOutraConta(string instancia, string chave, int status)
    {
        await using var db = CriarContexto();
        await SemearAsync(db);
        var bridge = new Bridge();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => new WebhookEvolutionService(db, new EvolutionApiClientTests.Protecao(), bridge)
            .ReceberAsync(JsonSerializer.SerializeToElement(new { instance = instancia, apikey = chave, @event = "messages.upsert", data = new { } }), null, null, null, default));
        Assert.Equal(status, erro.StatusCode);
        Assert.Empty(bridge.Recebidos);
    }

    [Fact]
    public async Task TextoRecebido_AdaptaContratoEIgnoraEcoProprio()
    {
        await using var db = CriarContexto();
        await SemearAsync(db);
        var bridge = new Bridge();
        var servico = new WebhookEvolutionService(db, new EvolutionApiClientTests.Protecao(), bridge);
        foreach (var proprio in new[] { false, true })
            await servico.ReceberAsync(JsonSerializer.SerializeToElement(new
            {
                instance = "consultorio", apikey = "chave", @event = "messages.upsert",
                data = new { key = new { id = "mensagem-1", fromMe = proprio, remoteJid = "5511999999999@s.whatsapp.net" }, message = new { conversation = "Olá" } }
            }), null, null, null, default);
        var recebido = Assert.Single(bridge.Recebidos);
        Assert.Equal("Olá", recebido.Conteudo);
        Assert.Equal("5511999999999", recebido.Telefone);
        Assert.Equal("mensagem.recebida", recebido.TipoEvento);
    }

    private static AppDbContext CriarContexto() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task SemearAsync(AppDbContext db)
    {
        var conta = new ContaPsicologo { Id = Guid.NewGuid(), NomeConta = "Teste", Status = StatusContaPsicologo.Ativa };
        db.Add(new IntegracaoWhatsApp { Id = Guid.NewGuid(), ContaPsicologoId = conta.Id, ContaPsicologo = conta,
            Provedor = "Evolution API", Instancia = "consultorio", TokenHash = "chave", Ativo = true });
        await db.SaveChangesAsync();
    }
    private sealed class Bridge : IBridgeWhatsAppService
    {
        public List<ReceberEventoBridgeRequest> Recebidos { get; } = [];
        public Task<ReceberEventoBridgeResponse> ReceberEventoAsync(string tokenBridge, ReceberEventoBridgeRequest request, string? ip, string? userAgent, CancellationToken cancellationToken)
        {
            Recebidos.Add(request);
            return Task.FromResult(new ReceberEventoBridgeResponse(Guid.NewGuid(), true, false, null, "Recebido"));
        }
    }
}

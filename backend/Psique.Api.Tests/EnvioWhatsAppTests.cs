using System.Net;
using Microsoft.EntityFrameworkCore;
using Psique.Api.Models;
using Psique.Api.Services.WhatsApp;
using Psique.Api.Shared.Enums;

namespace Psique.Api.Tests;

public sealed class EnvioWhatsAppTests(BancoIntegridadeRelacional banco) : IClassFixture<BancoIntegridadeRelacional>
{
    [SqlServerFact]
    public async Task ConfirmacaoAnteriorAoRetornoHttp_NaoSePerde()
    {
        var id = await SemearAsync();
        var fabrica = new EvolutionApiClientTests.Fabrica(async _ =>
        {
            await using var webhook = banco.CriarContexto();
            var mensagem = await webhook.MensagensWhatsApp.Include(m => m.ConversaWhatsApp).SingleAsync(m => m.Id == id);
            Assert.Null(mensagem.MensagemExternaId);
            webhook.EventosBridgeWhatsApp.Add(new EventoBridgeWhatsApp
            {
                Id = Guid.NewGuid(), ContaPsicologoId = mensagem.ContaPsicologoId,
                IntegracaoWhatsAppId = mensagem.ConversaWhatsApp.IntegracaoWhatsAppId,
                MensagemExternaId = "ack-antecipado", TipoEvento = "mensagem.lida",
                StatusMensagem = StatusMensagemWhatsApp.Lida, PayloadJson = "{}", Processado = true
            });
            await webhook.SaveChangesAsync();
            return new(HttpStatusCode.Created) { Content = new StringContent("{\"key\":{\"id\":\"ack-antecipado\"}}") };
        });
        await using var db = banco.CriarContexto();
        var protecao = new EvolutionApiClientTests.Protecao();
        await new EnvioWhatsAppService(db, new EvolutionApiClient(fabrica, protecao), protecao).ProcessarAsync(id, default);
        Assert.Equal(StatusMensagemWhatsApp.Lida, (await db.MensagensWhatsApp.SingleAsync(m => m.Id == id)).Status);
    }

    [SqlServerFact]
    public async Task DuasInstancias_EnviamUmaUnicaVezESomenteAposConfirmacao()
    {
        var id = await SemearAsync();
        var chamadas = 0;
        var fabrica = new EvolutionApiClientTests.Fabrica(async _ =>
        {
            Interlocked.Increment(ref chamadas);
            await Task.Delay(100);
            return new(HttpStatusCode.Created) { Content = new StringContent("{\"key\":{\"id\":\"aceite-real\"}}") };
        });
        async Task Processar()
        {
            await using var db = banco.CriarContexto();
            var protecao = new EvolutionApiClientTests.Protecao();
            await new EnvioWhatsAppService(db, new EvolutionApiClient(fabrica, protecao), protecao).ProcessarAsync(id, CancellationToken.None);
        }
        await Task.WhenAll(Processar(), Processar());
        Assert.Equal(1, chamadas);
        await using var conferir = banco.CriarContexto();
        var mensagem = await conferir.MensagensWhatsApp.SingleAsync(m => m.Id == id);
        Assert.Equal(StatusMensagemWhatsApp.Enviada, mensagem.Status);
        Assert.Equal("aceite-real", mensagem.MensagemExternaId);
    }

    [SqlServerFact]
    public async Task Timeout_NaoRepeteEnvioAutomaticamente()
    {
        var id = await SemearAsync();
        var chamadas = 0;
        var fabrica = new EvolutionApiClientTests.Fabrica(_ =>
        {
            Interlocked.Increment(ref chamadas);
            throw new HttpRequestException("Sem confirmação");
        });
        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            await using var db = banco.CriarContexto();
            var protecao = new EvolutionApiClientTests.Protecao();
            await new EnvioWhatsAppService(db, new EvolutionApiClient(fabrica, protecao), protecao).ProcessarAsync(id, CancellationToken.None);
        }
        Assert.Equal(1, chamadas);
        await using var conferir = banco.CriarContexto();
        Assert.Equal(StatusMensagemWhatsApp.Falhou, (await conferir.MensagensWhatsApp.SingleAsync(m => m.Id == id)).Status);
    }

    [SqlServerFact]
    public async Task ContaDesativadaDepoisDeEnfileirar_NaoEnvia()
    {
        var id = await SemearAsync();
        await using var db = banco.CriarContexto();
        var mensagem = await db.MensagensWhatsApp.Include(m => m.ContaPsicologo).SingleAsync(m => m.Id == id);
        mensagem.ContaPsicologo.Status = StatusContaPsicologo.Inativa;
        await db.SaveChangesAsync();
        var fabrica = new EvolutionApiClientTests.Fabrica(_ => throw new Exception("Não deveria enviar"));
        var protecao = new EvolutionApiClientTests.Protecao();
        await new EnvioWhatsAppService(db, new EvolutionApiClient(fabrica, protecao), protecao).ProcessarAsync(id, CancellationToken.None);
        Assert.Null(mensagem.EnvioIniciadoEm);
    }

    private async Task<Guid> SemearAsync()
    {
        await using var db = banco.CriarContexto();
        var conta = new ContaPsicologo { Id = Guid.NewGuid(), NomeConta = "Teste descartável", Status = StatusContaPsicologo.Ativa };
        var integracao = new IntegracaoWhatsApp
        {
            Id = Guid.NewGuid(), ContaPsicologoId = conta.Id, ContaPsicologo = conta,
            Nome = "Teste", Provedor = "Evolution API", Instancia = "teste", UrlBaseBridge = "https://bridge.test",
            TokenCriptografado = "chave-testes", TokenHash = Guid.NewGuid().ToString(), NumeroWhatsApp = "5511888888888",
            Status = StatusIntegracao.Ativa, Ativo = true
        };
        var conversa = new ConversaWhatsApp
        {
            Id = Guid.NewGuid(), ContaPsicologoId = conta.Id, IntegracaoWhatsAppId = integracao.Id,
            IntegracaoWhatsApp = integracao, Telefone = "5511999999999"
        };
        var mensagem = new MensagemWhatsApp
        {
            Id = Guid.NewGuid(), ContaPsicologoId = conta.Id, ConversaWhatsAppId = conversa.Id,
            ConversaWhatsApp = conversa, Direcao = DirecaoMensagem.Saida, Conteudo = "Lembrete administrativo de teste"
        };
        db.Add(mensagem);
        await db.SaveChangesAsync();
        return mensagem.Id;
    }
}

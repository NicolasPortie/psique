using System.Net;
using System.Text;
using Psique.Api.Models;
using Psique.Api.Services.Seguranca;
using Psique.Api.Services.WhatsApp;

namespace Psique.Api.Tests;

public sealed class EvolutionApiClientTests
{
    private static IntegracaoWhatsApp Integracao() => new()
    {
        Provedor = "Evolution API", Instancia = "consultorio teste",
        UrlBaseBridge = "https://bridge.test", TokenCriptografado = "segredo"
    };

    [Theory]
    [InlineData(200, "{\"instance\":{\"state\":\"open\"}}", true)]
    [InlineData(200, "{\"instance\":{\"state\":\"close\"}}", false)]
    [InlineData(401, "{}", false)]
    [InlineData(404, "{}", false)]
    [InlineData(200, "null", false)]
    [InlineData(200, "[]", false)]
    [InlineData(200, "{\"instance\":null}", false)]
    [InlineData(200, "{\"instance\":{\"state\":123}}", false)]
    public async Task Conexao_ExigeAutenticacaoEInstanciaAberta(int codigo, string corpo, bool esperado)
    {
        var fabrica = new Fabrica(async request =>
        {
            Assert.Equal("/instance/connectionState/consultorio%20teste", request.RequestUri!.AbsolutePath);
            Assert.Equal("segredo", request.Headers.GetValues("apikey").Single());
            await Task.CompletedTask;
            return new((HttpStatusCode)codigo) { Content = new StringContent(corpo) };
        });
        Assert.Equal(esperado, await new EvolutionApiClient(fabrica, new Protecao()).EstaConectadaAsync(Integracao(), CancellationToken.None));
    }

    [Fact]
    public async Task Envio_UsaContratoOficialERetornaIdentificadorDoProvedor()
    {
        var fabrica = new Fabrica(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/message/sendText/consultorio%20teste", request.RequestUri!.AbsolutePath);
            var corpo = await request.Content!.ReadAsStringAsync();
            Assert.Contains("5511999999999", corpo);
            Assert.Contains("text", corpo);
            return new(HttpStatusCode.Created) { Content = new StringContent("{\"key\":{\"id\":\"ID-REAL\"},\"status\":\"PENDING\"}", Encoding.UTF8, "application/json") };
        });
        Assert.Equal("ID-REAL", await new EvolutionApiClient(fabrica, new Protecao()).EnviarTextoAsync(Integracao(), "5511999999999", "Olá", CancellationToken.None));
    }

    [Theory]
    [InlineData(201, "{}")]
    [InlineData(201, "null")]
    [InlineData(201, "[]")]
    [InlineData(201, "{\"key\":null}")]
    [InlineData(401, "{\"key\":{\"id\":\"invalido\"}}")]
    public async Task Envio_SemConfirmacaoValidaNaoSimulaSucesso(int codigo, string corpo)
    {
        var fabrica = new Fabrica(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)codigo) { Content = new StringContent(corpo) }));
        await Assert.ThrowsAsync<HttpRequestException>(() => new EvolutionApiClient(fabrica, new Protecao()).EnviarTextoAsync(Integracao(), "5511999999999", "Olá", CancellationToken.None));
    }

    internal sealed class Fabrica(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request);
    }
    internal sealed class Protecao : IProtecaoDadosService
    {
        public string Proteger(string valor) => valor;
        public string Desproteger(string valor) => valor;
        public string HashDeterministico(string valor) => valor;
        public string HashToken(string valor) => valor;
        public string GerarTokenSeguro() => Guid.NewGuid().ToString();
    }
}

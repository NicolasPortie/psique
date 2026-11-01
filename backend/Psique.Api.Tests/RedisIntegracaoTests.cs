using Microsoft.Extensions.Options;
using Psique.Api.Configuracoes;
using Psique.Api.Services.Autenticacao;
using StackExchange.Redis;

namespace Psique.Api.Tests;

public sealed class RedisIntegracaoTests
{
    [RedisFact]
    public async Task Refresh_ConsumoUnicoERevogacaoEmOutraInstancia()
    {
        using var a = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("PSIQUE_TEST_REDIS")!);
        using var b = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("PSIQUE_TEST_REDIS")!);
        var config = Options.Create(new RedisConfiguracao { Prefixo = $"validacao:{Guid.NewGuid():N}:" });
        var primeira = new EstadoAutenticacaoRedis(a, config);
        var segunda = new EstadoAutenticacaoRedis(b, config);
        var estado = new EstadoRefreshToken(Guid.NewGuid(), Guid.NewGuid());
        await primeira.ArmazenarRefreshTokenAsync("rotacao", estado, TimeSpan.FromSeconds(10), default);
        var consumos = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? primeira : segunda).ConsumirRefreshTokenAsync("rotacao", default)));
        Assert.Single(consumos, item => item is not null);
        await primeira.ArmazenarRefreshTokenAsync("novo", estado, TimeSpan.FromSeconds(10), default);
        await primeira.ArmazenarRefreshTokenAsync("outra-sessao", estado, TimeSpan.FromSeconds(10), default);
        await segunda.RevogarRefreshTokensUsuarioAsync(estado.UsuarioId, default);
        Assert.Null(await primeira.ConsumirRefreshTokenAsync("novo", default));
        Assert.Null(await segunda.ConsumirRefreshTokenAsync("outra-sessao", default));
    }

    [RedisFact]
    public async Task DuasInstancias_ConsomemTokenUmaVezEExpiramTentativas()
    {
        using var a = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("PSIQUE_TEST_REDIS")!);
        using var b = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("PSIQUE_TEST_REDIS")!);
        var config = Options.Create(new RedisConfiguracao { Prefixo = $"validacao:{Guid.NewGuid():N}:" });
        var primeira = new EstadoAutenticacaoRedis(a, config);
        var segunda = new EstadoAutenticacaoRedis(b, config);
        var usuario = Guid.NewGuid();
        await primeira.ArmazenarTokenRecuperacaoAsync("recuperacao", usuario, TimeSpan.FromSeconds(2), default);
        var consumos = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? primeira : segunda).ConsumirTokenRecuperacaoAsync("recuperacao", default)));
        Assert.Single(consumos, id => id == usuario);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? primeira : segunda).RegistrarTentativaLoginAsync("contato", TimeSpan.FromMilliseconds(300), default)));
        Assert.Equal(20, await segunda.ObterTentativasLoginAsync("contato", default));
        await Task.Delay(400);
        Assert.Equal(0, await primeira.ObterTentativasLoginAsync("contato", default));
    }

    [RedisFact]
    public async Task ConfirmacaoEmail_ConsumoAtomicoETentativasLimitadas()
    {
        using var a = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("PSIQUE_TEST_REDIS")!);
        var config = Options.Create(new RedisConfiguracao { Prefixo = $"validacao:{Guid.NewGuid():N}:" });
        var primeira = new EstadoConfirmacaoEmailRedis(a, config);
        var segunda = new EstadoConfirmacaoEmailRedis(a, config);
        await primeira.ArmazenarAsync("cadastro", "hash", 3, TimeSpan.FromSeconds(5), default);
        var consumos = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? primeira : segunda).ConsumirAsync("cadastro", "hash", default)));
        Assert.Single(consumos, ok => ok);
        await primeira.ArmazenarAsync("cadastro", "hash", 2, TimeSpan.FromSeconds(5), default);
        Assert.False(await segunda.ConsumirAsync("cadastro", "errado", default));
        Assert.False(await primeira.ConsumirAsync("cadastro", "errado", default));
        Assert.False(await segunda.ConsumirAsync("cadastro", "hash", default));
    }
}

public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PSIQUE_TEST_REDIS")))
            Skip = "Defina PSIQUE_TEST_REDIS para executar com Redis real e isolado.";
    }
}

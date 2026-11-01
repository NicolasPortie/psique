using System.Text.Json;
using Psique.Api.Configuracoes;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Psique.Api.Services.Autenticacao;

public sealed class EstadoAutenticacaoRedis(
    IConnectionMultiplexer redis,
    IOptions<RedisConfiguracao> configuracao)
    : IEstadoAutenticacao
{
    private readonly IDatabase _database = redis.GetDatabase();
    private readonly string _prefixo = configuracao.Value.Prefixo;

    public async Task<long> ObterTentativasLoginAsync(
        string identificador,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valor = await _database.StringGetAsync(ChaveTentativa(identificador));
        return valor.TryParse(out long tentativas) ? tentativas : 0;
    }

    public async Task<long> RegistrarTentativaLoginAsync(
        string identificador,
        TimeSpan expiracao,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string script = """
            local atual = redis.call('INCR', KEYS[1])
            if atual == 1 then
                redis.call('PEXPIRE', KEYS[1], ARGV[1])
            end
            return atual
            """;

        var resultado = await _database.ScriptEvaluateAsync(
            script,
            [ChaveTentativa(identificador)],
            [(long)expiracao.TotalMilliseconds]);

        return (long)resultado;
    }

    public async Task LimparTentativasLoginAsync(
        string identificador,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.KeyDeleteAsync(ChaveTentativa(identificador));
    }

    public async Task ArmazenarTokenRecuperacaoAsync(
        string tokenHash,
        Guid usuarioId,
        TimeSpan expiracao,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.StringSetAsync(
            ChaveRecuperacao(tokenHash),
            usuarioId.ToString(),
            expiracao,
            When.Always);
    }

    public async Task<Guid?> ConsumirTokenRecuperacaoAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        var valor = await ObterERemoverAsync(ChaveRecuperacao(tokenHash), cancellationToken);
        return Guid.TryParse(valor, out var usuarioId) ? usuarioId : null;
    }

    public async Task RemoverTokenRecuperacaoAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.KeyDeleteAsync(ChaveRecuperacao(tokenHash));
    }

    public async Task ArmazenarRefreshTokenAsync(
        string tokenHash,
        EstadoRefreshToken estado,
        TimeSpan expiracao,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chaveToken = ChaveRefresh(tokenHash);
        var chaveUsuario = ChaveRefreshUsuario(estado.UsuarioId);
        var transacao = _database.CreateTransaction();

        _ = transacao.StringSetAsync(
            chaveToken,
            JsonSerializer.Serialize(estado),
            expiracao,
            When.Always);
        _ = transacao.SetAddAsync(chaveUsuario, tokenHash);
        _ = transacao.KeyExpireAsync(chaveUsuario, expiracao);

        if (!await transacao.ExecuteAsync())
        {
            throw new RedisException("Não foi possível persistir a sessão.");
        }
    }

    public async Task<EstadoRefreshToken?> ConsumirRefreshTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        var valor = await ObterERemoverAsync(ChaveRefresh(tokenHash), cancellationToken);
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var estado = JsonSerializer.Deserialize<EstadoRefreshToken>(valor);
        if (estado is not null)
        {
            await _database.SetRemoveAsync(ChaveRefreshUsuario(estado.UsuarioId), tokenHash);
        }

        return estado;
    }

    public async Task RevogarRefreshTokensUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chaveUsuario = ChaveRefreshUsuario(usuarioId);
        const string script = """
            local tokens = redis.call('SMEMBERS', KEYS[1])
            for _, token in ipairs(tokens) do
                redis.call('DEL', ARGV[1] .. token)
            end
            redis.call('DEL', KEYS[1])
            return #tokens
            """;
        await _database.ScriptEvaluateAsync(script, [chaveUsuario], [$"{_prefixo}auth:refresh:"]);

    }

    private async Task<string?> ObterERemoverAsync(
        RedisKey chave,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string script = """
            local valor = redis.call('GET', KEYS[1])
            if valor then
                redis.call('DEL', KEYS[1])
            end
            return valor
            """;

        var resultado = await _database.ScriptEvaluateAsync(script, [chave]);
        return resultado.IsNull ? null : resultado.ToString();
    }

    private RedisKey ChaveTentativa(string identificador) =>
        $"{_prefixo}auth:tentativas:{identificador}";

    private RedisKey ChaveRecuperacao(string tokenHash) =>
        $"{_prefixo}auth:recuperacao:{tokenHash}";

    private RedisKey ChaveRefresh(string tokenHash) =>
        $"{_prefixo}auth:refresh:{tokenHash}";

    private RedisKey ChaveRefreshUsuario(Guid usuarioId) =>
        $"{_prefixo}auth:refresh-usuario:{usuarioId}";
}

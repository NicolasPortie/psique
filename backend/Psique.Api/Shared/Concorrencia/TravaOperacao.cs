using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Psique.Api.Data;
using Psique.Api.Shared.Excecoes;

namespace Psique.Api.Shared.Concorrencia;

/// <summary>Exclusão por recurso entre instâncias da API que compartilham o SQL Server.</summary>
public sealed class TravaOperacao : IAsyncDisposable
{
    private readonly AppDbContext? db;
    private readonly string recurso;
    private readonly bool fechar;
    private TravaOperacao(AppDbContext? contexto, string nome, bool fecharConexao) => (db, recurso, fechar) = (contexto, nome, fecharConexao);

    public static async Task<TravaOperacao> AdquirirAsync(AppDbContext db, string recurso, CancellationToken ct)
    {
        // Os testes unitários usam InMemory; a exclusão distribuída é verificada em SQL Server real.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory") return new(null, recurso, false);
        var fechar = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (fechar) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var comando = db.Database.GetDbConnection().CreateCommand();
            comando.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            comando.CommandText = "DECLARE @resultado int; EXEC @resultado = sys.sp_getapplock @Resource=@recurso, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=15000; SELECT @resultado;";
            var parametro = comando.CreateParameter(); parametro.ParameterName = "@recurso"; parametro.Value = $"Psique:{recurso}";
            comando.Parameters.Add(parametro);
            var resultado = Convert.ToInt32(await comando.ExecuteScalarAsync(ct));
            if (resultado < 0) throw new RegraNegocioException("Esta operação está sendo concluída em outro acesso. Tente novamente.", StatusCodes.Status409Conflict);
            return new(db, recurso, fechar);
        }
        catch
        {
            if (fechar) await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (db is null) return;
        try
        {
            await using var comando = db.Database.GetDbConnection().CreateCommand();
            comando.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            comando.CommandText = "EXEC sys.sp_releaseapplock @Resource=@recurso, @LockOwner='Session';";
            var parametro = comando.CreateParameter(); parametro.ParameterName = "@recurso"; parametro.Value = $"Psique:{recurso}";
            comando.Parameters.Add(parametro);
            await comando.ExecuteNonQueryAsync(CancellationToken.None);
        }
        finally { if (fechar) await db.Database.CloseConnectionAsync(); }
    }
}

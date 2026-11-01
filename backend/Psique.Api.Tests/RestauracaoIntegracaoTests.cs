using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Psique.Api.Tests;

public sealed class RestauracaoIntegracaoTests(BancoIntegridadeRelacional banco) : IClassFixture<BancoIntegridadeRelacional>
{
    [RedisFact]
    public async Task ChaveiroExportadoERestaurado_ComCertificado_ReabreDadoCifrado()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("PSIQUE_TEST_REDIS")!);
        var origem = $"validacao:{Guid.NewGuid():N}:chaves";
        var destino = $"validacao:{Guid.NewGuid():N}:restauradas";
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=restauracao-teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var restaurado = X509CertificateLoader.LoadPkcs12(certificado.Export(X509ContentType.Pfx, "teste"), "teste");
        ServiceProvider Criar(string chave, X509Certificate2 cert)
        {
            var servicos = new ServiceCollection();
            servicos.AddLogging();
            servicos.AddDataProtection().SetApplicationName("ValidacaoRestauracao")
                .PersistKeysToStackExchangeRedis(redis, chave).ProtectKeysWithCertificate(cert);
            return servicos.BuildServiceProvider();
        }
        try
        {
            string cifrado;
            using (var antes = Criar(origem, certificado))
                cifrado = antes.GetRequiredService<IDataProtectionProvider>().CreateProtector("dado-teste").Protect("registro descartável");
            var exportado = await redis.GetDatabase().ListRangeAsync(origem);
            Assert.NotEmpty(exportado);
            Assert.Contains("encryptedSecret", exportado[0].ToString());
            await redis.GetDatabase().ListRightPushAsync(destino, exportado);
            using var depois = Criar(destino, restaurado);
            Assert.Equal("registro descartável", depois.GetRequiredService<IDataProtectionProvider>().CreateProtector("dado-teste").Unprotect(cifrado));
        }
        finally { await redis.GetDatabase().KeyDeleteAsync([origem, destino]); }
    }

    [DockerSqlFact]
    public async Task BackupComChecksum_RestauraBancoIsoladoEPreservaRegistro()
    {
        await using var db = banco.CriarContexto();
        var original = db.Database.GetDbConnection().Database;
        Assert.StartsWith("Psique_Ticket01_", original);
        var novo = $"Psique_Restore_{Guid.NewGuid():N}";
        var arquivo = $"/var/opt/mssql/data/{novo}.bak";
        var identificador = Guid.NewGuid();
        db.ContasPsicologo.Add(new() { Id = identificador, NomeConta = "Backup de teste" });
        await db.SaveChangesAsync();
        var arquivos = await db.Database.SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.database_files ORDER BY type").ToArrayAsync();
        var builder = new SqlConnectionStringBuilder(db.Database.GetConnectionString()) { InitialCatalog = "master" };
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
        async Task Executar(string sql)
        {
            await using var comando = new SqlCommand(sql, master) { CommandTimeout = 120 };
            await comando.ExecuteNonQueryAsync();
        }
        try
        {
            await Executar($"BACKUP DATABASE [{original}] TO DISK=N'{arquivo}' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK=N'{arquivo}' WITH CHECKSUM;");
            var dados = arquivos[0].Replace("'", "''");
            var log = arquivos[1].Replace("'", "''");
            await Executar($"RESTORE DATABASE [{novo}] FROM DISK=N'{arquivo}' WITH CHECKSUM, MOVE N'{dados}' TO N'/var/opt/mssql/data/{novo}.mdf', MOVE N'{log}' TO N'/var/opt/mssql/data/{novo}.ldf';");
            await using var consulta = new SqlCommand($"SELECT COUNT(*) FROM [{novo}].dbo.ContasPsicologo WHERE Id=@id", master);
            consulta.Parameters.AddWithValue("@id", identificador);
            Assert.Equal(1, Convert.ToInt32(await consulta.ExecuteScalarAsync()));
        }
        finally
        {
            // Nome gerado acima e exclusivo do teste; nunca substitui o banco da aplicação.
            await Executar($"IF DB_ID(N'{novo}') IS NOT NULL BEGIN ALTER DATABASE [{novo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{novo}]; END");
        }
    }
}

public sealed class DockerSqlFactAttribute : FactAttribute
{
    public DockerSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PSIQUE_TEST_SQL_DOCKER") != "1")
            Skip = "Exige SQL Server Linux descartável: PSIQUE_TEST_SQL_DOCKER=1.";
    }
}

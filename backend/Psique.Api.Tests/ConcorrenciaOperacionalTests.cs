using Microsoft.EntityFrameworkCore;
using Psique.Api.Data;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Concorrencia;

namespace Psique.Api.Tests;

public sealed class ConcorrenciaOperacionalTests(BancoIntegridadeRelacional banco) : IClassFixture<BancoIntegridadeRelacional>
{
    [SqlServerFact]
    public async Task Trava_DentroDaTransacaoDaConsulta_UsaAMesmaTransacao()
    {
        await using var db = banco.CriarContexto();
        await using var transacao = await db.Database.BeginTransactionAsync();
        await using (var trava = await TravaOperacao.AdquirirAsync(db, $"consulta:{Guid.NewGuid():N}", default))
            Assert.NotNull(db.Database.CurrentTransaction);
        await transacao.CommitAsync();
    }

    [SqlServerFact]
    public async Task Mensalidades_InstanciasConcorrentesGeramUmaCobrancaPorPaciente()
    {
        var conta = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        await using (var preparar = banco.CriarContexto())
        {
            preparar.AddRange(new Psique.Api.Models.ContaPsicologo { Id = conta },
                new Psique.Api.Models.Usuario { Id = usuario, ContaPsicologoId = conta, Email = $"{usuario:N}@teste.invalid", NomeCompleto = "Teste", SenhaHash = "hash", Perfil = Psique.Api.Shared.Enums.PerfilUsuario.Psicologo },
                new Psique.Api.Models.Paciente { Id = Guid.NewGuid(), ContaPsicologoId = conta, NomeCompleto = "Mensalista", ModoCobranca = Psique.Api.Shared.Enums.ModoCobranca.Mensal, ValorSessao = 720 });
            await preparar.SaveChangesAsync();
        }
        async Task Gerar()
        {
            await using var db = banco.CriarContexto();
            var servico = new Psique.Api.Services.Pagamentos.PagamentosService(db, new AutorizacaoContaService(db), new Auditoria(), new EvolutionApiClientTests.Protecao());
            await servico.GerarMensalidadesAsync(usuario, conta, new() { Ano = 2026, Mes = 11, DiaVencimento = 10 }, null, null, default);
        }
        await Task.WhenAll(Gerar(), Gerar(), Gerar());
        await using var conferir = banco.CriarContexto();
        Assert.Single(await conferir.Pagamentos.Where(p => p.ContaPsicologoId == conta).ToArrayAsync());
    }

    private sealed class Auditoria : IAuditoriaService
    {
        public void Registrar(Guid contaPsicologoId, Guid? usuarioId, string tipoAcao, string entidade, Guid? entidadeId, string descricao, object? dadosAntes = null, object? dadosDepois = null, string? ip = null, string? userAgent = null) { }
    }

    [SqlServerFact]
    public async Task MesmoRecurso_ExecutaEmSerieEntreConexoesIndependentes()
    {
        var recurso = $"teste:{Guid.NewGuid():N}";
        var simultaneos = 0;
        var maximo = 0;
        async Task Executar()
        {
            await using var db = banco.CriarContexto();
            await using var trava = await TravaOperacao.AdquirirAsync(db, recurso, CancellationToken.None);
            var atual = Interlocked.Increment(ref simultaneos);
            maximo = Math.Max(maximo, atual);
            await Task.Delay(50);
            Interlocked.Decrement(ref simultaneos);
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Executar()));
        Assert.Equal(1, maximo);
    }

    [SqlServerFact]
    public async Task RecursosDiferentes_NaoBloqueiamOutrosConsultorios()
    {
        await using var primeiro = banco.CriarContexto();
        await using var segundo = banco.CriarContexto();
        await using var travaA = await TravaOperacao.AdquirirAsync(primeiro, $"a:{Guid.NewGuid():N}", CancellationToken.None);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var travaB = await TravaOperacao.AdquirirAsync(segundo, $"b:{Guid.NewGuid():N}", limite.Token);
        Assert.False(limite.IsCancellationRequested);
    }
}

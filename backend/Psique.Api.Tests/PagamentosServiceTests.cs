using Microsoft.EntityFrameworkCore;
using Psique.Api.Data;
using Psique.Api.DTOs.Pagamentos;
using Psique.Api.Models;
using Psique.Api.Services.Pagamentos;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;

namespace Psique.Api.Tests;

public sealed class PagamentosServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Listar_RecortaDatasNoFusoDaConta()
    {
        await using var c = await Cenario.Criar();
        c.Adicionar(StatusPagamento.Pendente, new DateTimeOffset(2026, 10, 1, 0, 30, 0, TimeSpan.FromHours(-5)));
        await c.Db.SaveChangesAsync();
        var resultado = await c.Service.ListarAsync(c.Usuario.Id, c.ContaId,
            new(null, null, new(2026, 10, 1), new(2026, 10, 1), null, false), 1, 20, Ct);
        Assert.Single(resultado.Itens);
        Assert.Equal(180, resultado.Resumo.Pendente);
    }

    [Fact]
    public async Task ExportarEReciboMensal_IncluemRecebimentoDoUltimoDiaNoFusoDaConta()
    {
        await using var c = await Cenario.Criar();
        c.AdicionarProfissional();
        var pagamento = c.Adicionar(StatusPagamento.Pago);
        pagamento.PagoEm = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.FromHours(-5));
        await c.Db.SaveChangesAsync();
        var exportados = await c.Service.ExportarRecebimentosAsync(c.Usuario.Id, c.ContaId, new(2026, 10, 1), new(2026, 10, 31), null, null, Ct);
        Assert.Single(exportados);
        var recibo = await c.Service.ObterReciboMensalAsync(c.Usuario.Id, c.ContaId, c.Paciente.Id, 2026, 10, null, null, Ct);
        Assert.Single(recibo.Itens);
        Assert.Equal(180, recibo.Total);
    }

    [Fact]
    public async Task GerarMensalidades_UsaFusoDaContaESemDuplicar()
    {
        await using var c = await Cenario.Criar();
        c.Paciente.ModoCobranca = ModoCobranca.Mensal;
        c.Paciente.ValorSessao = 720;
        await c.Db.SaveChangesAsync();
        var request = new GerarMensalidadesRequest { Ano = 2026, Mes = 10, DiaVencimento = 10 };
        await c.Service.GerarMensalidadesAsync(c.Usuario.Id, c.ContaId, request, null, null, Ct);
        await c.Service.GerarMensalidadesAsync(c.Usuario.Id, c.ContaId, request, null, null, Ct);
        var pagamento = Assert.Single(c.Db.Pagamentos);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(-5)), pagamento.VencimentoEm);
    }

    [Theory]
    [InlineData(StatusPagamento.Pendente, StatusPagamento.Reembolsado)]
    [InlineData(StatusPagamento.Cancelado, StatusPagamento.Pago)]
    [InlineData(StatusPagamento.Reembolsado, StatusPagamento.Pago)]
    [InlineData(StatusPagamento.Pago, StatusPagamento.Cancelado)]
    [InlineData(StatusPagamento.Pago, StatusPagamento.Pago)]
    public async Task AlterarStatus_RecusaTransicaoInvalidaSemAlterarPagamento(StatusPagamento anterior, StatusPagamento novo)
    {
        await using var c = await Cenario.Criar();
        var pagamento = c.Adicionar(anterior);
        var pagoEm = pagamento.PagoEm;
        await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RegraNegocioException>(() => c.Service.AlterarStatusAsync(c.Usuario.Id, c.ContaId, pagamento.Id, new() { Status = novo }, null, null, Ct));
        Assert.Equal(anterior, pagamento.Status);
        Assert.Equal(pagoEm, pagamento.PagoEm);
    }

    [Fact]
    public async Task Reembolsar_PreservaDataDoRecebimento()
    {
        await using var c = await Cenario.Criar();
        var pagamento = c.Adicionar(StatusPagamento.Pago);
        var pagoEm = pagamento.PagoEm;
        await c.Db.SaveChangesAsync();
        var resultado = await c.Service.AlterarStatusAsync(c.Usuario.Id, c.ContaId, pagamento.Id, new() { Status = StatusPagamento.Reembolsado }, null, null, Ct);
        Assert.Equal(pagoEm, resultado.PagoEm);
    }

    [Theory]
    [InlineData("", "protegido:06123456", "hash:06123456")]
    [InlineData("Ana Silva", "", "")]
    [InlineData("Ana Silva", "protegido:123", "hash:123")]
    public async Task Recibos_RecusamIdentificacaoProfissionalIncompleta(string nome, string crp, string hash)
    {
        await using var c = await Cenario.Criar();
        c.AdicionarProfissional(nome, crp, hash);
        var pagamento = c.Adicionar(StatusPagamento.Pago);
        await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RegraNegocioException>(() => c.Service.ObterReciboAsync(c.Usuario.Id, c.ContaId, pagamento.Id, null, null, Ct));
        var data = pagamento.PagoEm!.Value;
        await Assert.ThrowsAsync<RegraNegocioException>(() => c.Service.ObterReciboMensalAsync(c.Usuario.Id, c.ContaId, c.Paciente.Id, data.Year, data.Month, null, null, Ct));
    }

    [Fact]
    public async Task Listar_RecusaPeriodoInvertido()
    {
        await using var c = await Cenario.Criar();
        await Assert.ThrowsAsync<RegraNegocioException>(() => c.Service.ListarAsync(c.Usuario.Id, c.ContaId,
            new(null, null, new(2026, 10, 31), new(2026, 10, 1), null, false), 1, 20, Ct));
    }

    [Fact]
    public async Task AlterarStatus_NaoAlteraPagamentoDeOutraConta()
    {
        await using var c = await Cenario.Criar();
        var pagamento = c.Adicionar(StatusPagamento.Pendente);
        await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RegraNegocioException>(() => c.Service.AlterarStatusAsync(c.Usuario.Id, Guid.NewGuid(), pagamento.Id, new() { Status = StatusPagamento.Pago }, null, null, Ct));
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
    }

    [Fact]
    public async Task AlterarStatus_RecusaRecebimentoConcorrenteComCancelamento()
    {
        await using var c = await Cenario.Criar();
        var pagamento = c.Adicionar(StatusPagamento.Pendente);
        await c.Db.SaveChangesAsync();
        await using var outroContexto = c.CriarOutroContexto();
        await outroContexto.Pagamentos.SingleAsync(x => x.Id == pagamento.Id);
        await c.Service.AlterarStatusAsync(c.Usuario.Id, c.ContaId, pagamento.Id, new() { Status = StatusPagamento.Cancelado }, null, null, Ct);
        var outroService = new PagamentosService(outroContexto, new AutorizacaoContaService(outroContexto), new AuditoriaFake(), new ProtecaoFake());
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => outroService.AlterarStatusAsync(c.Usuario.Id, c.ContaId, pagamento.Id, new() { Status = StatusPagamento.Pago }, null, null, Ct));
        Assert.Equal(409, erro.StatusCode);
        await using var leitura = c.CriarOutroContexto();
        Assert.Equal(StatusPagamento.Cancelado, (await leitura.Pagamentos.SingleAsync()).Status);
    }

    private sealed class Cenario : IAsyncDisposable
    {
        private readonly DbContextOptions<AppDbContext> opcoes = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public AppDbContext Db { get; }
        public Cenario() => Db = new(opcoes);
        public AppDbContext CriarOutroContexto() => new(opcoes);
        public Usuario Usuario { get; } = new() { Id = Guid.NewGuid(), ContaPsicologoId = Guid.NewGuid(), Email = "ana@teste.local", SenhaHash = "hash", Perfil = PerfilUsuario.Psicologo };
        public Guid ContaId => Usuario.ContaPsicologoId;
        public Paciente Paciente { get; private set; } = null!;
        public PagamentosService Service => new(Db, new AutorizacaoContaService(Db), new AuditoriaFake(), new ProtecaoFake());

        public static async Task<Cenario> Criar()
        {
            var c = new Cenario();
            c.Paciente = new() { Id = Guid.NewGuid(), ContaPsicologoId = c.ContaId, NomeCompleto = "Paciente" };
            c.Db.AddRange(new ContaPsicologo { Id = c.ContaId }, c.Usuario, c.Paciente,
                new ConfiguracaoAtendimento { Id = Guid.NewGuid(), ContaPsicologoId = c.ContaId, FusoHorario = "America/Rio_Branco" });
            await c.Db.SaveChangesAsync();
            return c;
        }

        public Pagamento Adicionar(StatusPagamento status, DateTimeOffset? vencimento = null)
        {
            var item = new Pagamento { Id = Guid.NewGuid(), ContaPsicologoId = ContaId, PacienteId = Paciente.Id, Status = status, Valor = 180, VencimentoEm = vencimento,
                PagoEm = status == StatusPagamento.Pago ? new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(-5)) : null };
            Db.Add(item);
            return item;
        }

        public void AdicionarProfissional(string nome = "Ana Silva", string crp = "protegido:06123456", string hash = "hash:06123456") =>
            Db.Add(new DadosProfissionaisPsicologo { Id = Guid.NewGuid(), ContaPsicologoId = ContaId, UsuarioId = Usuario.Id, NomeProfissional = nome, Crp = crp, CrpHash = hash });

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class ProtecaoFake : IProtecaoDadosService
    {
        public string Proteger(string valor) => $"protegido:{valor}";
        public string Desproteger(string valor) => valor.Replace("protegido:", "");
        public string HashDeterministico(string valor) => $"hash:{valor}";
        public string HashToken(string valor) => throw new NotSupportedException();
        public string GerarTokenSeguro() => throw new NotSupportedException();
    }

    private sealed class AuditoriaFake : IAuditoriaService
    {
        public void Registrar(Guid contaPsicologoId, Guid? usuarioId, string tipoAcao, string entidade, Guid? entidadeId, string descricao, object? dadosAntes = null, object? dadosDepois = null, string? ip = null, string? userAgent = null) { }
    }
}

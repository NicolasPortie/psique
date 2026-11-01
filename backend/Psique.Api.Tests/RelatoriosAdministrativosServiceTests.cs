using Psique.Api.Data;
using Psique.Api.DTOs.Relatorios;
using Psique.Api.Models;
using Psique.Api.Services.Relatorios;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Psique.Api.Tests;

public sealed class RelatoriosAdministrativosServiceTests
{
    [Fact]
    public async Task Relatorio_DeveExibirDataEHoraNoFusoDaConta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        contexto.Add(new ConfiguracaoAtendimento { Id = Guid.NewGuid(), ContaPsicologoId = cenario.ContaId, FusoHorario = "America/Rio_Branco" });
        var consulta = await contexto.Consultas.SingleAsync(x => x.Id == cenario.Consulta.Id);
        consulta.InicioEm = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.FromHours(-5)).ToUniversalTime();
        consulta.FimEm = consulta.InicioEm.AddMinutes(50);
        consulta.Status = StatusConsulta.Confirmada;
        await contexto.SaveChangesAsync();
        var resultado = await CriarService(contexto).GerarAsync(cenario.UsuarioId, cenario.ContaId, "consultas-agendadas",
            new() { PeriodoInicio = consulta.InicioEm.AddHours(-1), PeriodoFim = consulta.FimEm.AddHours(1) }, null, null, CancellationToken.None);
        Assert.Contains("31/10/2026 23:30", resultado.Html);
        Assert.DoesNotContain("01/11/2026 01:30", resultado.Html);
    }

    [Theory]
    [InlineData("valores-recebidos")]
    [InlineData("pagamentos-pendentes")]
    [InlineData("pacientes-status")]
    public async Task Relatorios_RespeitamPermissoesDosDadosDeOrigem(string codigo)
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var perfil = new PerfilAcesso { Id = Guid.NewGuid(), ContaPsicologoId = cenario.ContaId, Nome = "Agenda e relatórios", Permissoes = "Agenda,Relatorios" };
        var usuario = await contexto.Usuarios.SingleAsync(x => x.Id == cenario.UsuarioId);
        usuario.Perfil = PerfilUsuario.Personalizado;
        usuario.PerfilAcessoId = perfil.Id;
        contexto.Add(perfil);
        await contexto.SaveChangesAsync();
        var service = CriarService(contexto);
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => service.GerarAsync(cenario.UsuarioId, cenario.ContaId, codigo,
            new() { PeriodoInicio = DateTimeOffset.UtcNow.AddDays(-7), PeriodoFim = DateTimeOffset.UtcNow }, null, null, CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, erro.StatusCode);
        var catalogo = await service.ListarCatalogoAsync(cenario.UsuarioId, cenario.ContaId, CancellationToken.None);
        Assert.DoesNotContain(catalogo, x => x.Codigo == codigo);
        Assert.Contains(catalogo, x => x.Codigo == "consultas-agendadas");
    }

    [Fact]
    public async Task Gerar_SessoesRealizadas_DeveRetornarHtmlComCabecalhoETabela()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        contexto.RegistrosSessao.Add(new RegistroSessao
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = cenario.ContaId,
            ProntuarioId = cenario.ProntuarioId,
            PacienteId = cenario.Paciente.Id,
            ConsultaId = cenario.Consulta.Id,
            TipoAtendimentoId = cenario.TipoId,
            CriadoPorUsuarioId = cenario.UsuarioId,
            DataSessao = DateTimeOffset.UtcNow.AddDays(-1),
            Status = StatusRegistroSessao.Finalizado
        });
        await contexto.SaveChangesAsync();

        var service = CriarService(contexto);
        var resultado = await service.GerarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            "sessoes-realizadas",
            new GerarRelatorioRequest
            {
                PeriodoInicio = DateTimeOffset.UtcNow.AddDays(-7),
                PeriodoFim = DateTimeOffset.UtcNow.AddDays(1)
            },
            null,
            null,
            CancellationToken.None);

        Assert.Contains("Sessões realizadas", resultado.Titulo);
        Assert.Contains("<!DOCTYPE html>", resultado.Html);
        Assert.Contains("Psique", resultado.Html);
        Assert.Contains(cenario.Paciente.NomeCompleto, resultado.Html);
        Assert.DoesNotContain("ObservacoesClinicas", resultado.Html);
    }

    [Fact]
    public async Task Gerar_NaoDeveExporDadosDeOutraConta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var outraConta = Guid.NewGuid();
        var outroPaciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraConta,
            NomeCompleto = "Paciente externo secreto",
            Status = StatusPaciente.Ativo
        };
        contexto.AddRange(
            new ContaPsicologo { Id = outraConta, NomeConta = "Outra", Status = StatusContaPsicologo.Ativa },
            outroPaciente);
        await contexto.SaveChangesAsync();

        var service = CriarService(contexto);
        var resultado = await service.GerarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            "pacientes-status",
            new GerarRelatorioRequest
            {
                PeriodoInicio = DateTimeOffset.UtcNow.AddDays(-30),
                PeriodoFim = DateTimeOffset.UtcNow
            },
            null,
            null,
            CancellationToken.None);

        Assert.Contains(cenario.Paciente.NomeCompleto, resultado.Html);
        Assert.DoesNotContain("Paciente externo secreto", resultado.Html);
    }

    [Fact]
    public async Task HistoricoPaciente_ExigePaciente()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var service = CriarService(contexto);

        var excecao = await Assert.ThrowsAsync<RegraNegocioException>(() => service.GerarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            "historico-atendimentos-paciente",
            new GerarRelatorioRequest
            {
                PeriodoInicio = DateTimeOffset.UtcNow.AddDays(-30),
                PeriodoFim = DateTimeOffset.UtcNow
            },
            null,
            null,
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, excecao.StatusCode);
    }

    [Fact]
    public async Task MesEmNumeros_ContaNoFusoDaContaESomaPagamentosDoMes()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var brasilia = TimeSpan.FromHours(-3);
        Consulta Em(DateTimeOffset inicio, StatusConsulta status) => new()
        {
            Id = Guid.NewGuid(), ContaPsicologoId = cenario.ContaId, PacienteId = cenario.Paciente.Id, TipoAtendimentoId = cenario.TipoId,
            InicioEm = inicio, FimEm = inicio.AddMinutes(50), Status = status, Modalidade = ModalidadeAtendimento.Presencial
        };
        Pagamento Pagamento(decimal valor, StatusPagamento status, DateTimeOffset vencimento) => new()
        {
            Id = Guid.NewGuid(), ContaPsicologoId = cenario.ContaId, PacienteId = cenario.Paciente.Id, Valor = valor, Status = status,
            VencimentoEm = vencimento, PagoEm = status == StatusPagamento.Pago ? vencimento : null
        };
        contexto.AddRange(
            Em(new DateTimeOffset(2026, 3, 10, 9, 0, 0, brasilia), StatusConsulta.Realizada),
            // 23h30 de 31/03 em Brasília já é 1º de abril em UTC: continua sendo março para o consultório.
            Em(new DateTimeOffset(2026, 3, 31, 23, 30, 0, brasilia), StatusConsulta.Realizada),
            Em(new DateTimeOffset(2026, 3, 12, 9, 0, 0, brasilia), StatusConsulta.NaoCompareceu),
            Em(new DateTimeOffset(2026, 3, 13, 9, 0, 0, brasilia), StatusConsulta.Cancelada),
            Em(new DateTimeOffset(2026, 4, 1, 9, 0, 0, brasilia), StatusConsulta.Realizada),
            Pagamento(200, StatusPagamento.Pago, new DateTimeOffset(2026, 3, 10, 12, 0, 0, brasilia)),
            Pagamento(150, StatusPagamento.Pendente, new DateTimeOffset(2026, 3, 15, 12, 0, 0, brasilia)),
            Pagamento(90, StatusPagamento.Cancelado, new DateTimeOffset(2026, 3, 16, 12, 0, 0, brasilia)));
        await contexto.SaveChangesAsync();

        var numeros = await CriarService(contexto).ObterMesEmNumerosAsync(cenario.UsuarioId, cenario.ContaId, 2026, 3, CancellationToken.None);

        Assert.Equal(2, numeros.SessoesRealizadas);
        Assert.Equal(1, numeros.PacientesAtendidos);
        Assert.Equal(1, numeros.Faltas);
        Assert.Equal(33, numeros.PercentualFaltas);
        Assert.Equal(1, numeros.Cancelamentos);
        Assert.Equal(200, numeros.Recebido);
        Assert.Equal(150, numeros.AReceber);
        Assert.Equal(150, numeros.Vencido);
    }

    [Fact]
    public async Task MesEmNumeros_SemPermissaoFinanceiro_NaoTrazValores()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var perfil = new PerfilAcesso { Id = Guid.NewGuid(), ContaPsicologoId = cenario.ContaId, Nome = "Agenda e relatórios", Permissoes = "Agenda,Relatorios" };
        var usuario = await contexto.Usuarios.SingleAsync(x => x.Id == cenario.UsuarioId);
        usuario.Perfil = PerfilUsuario.Personalizado;
        usuario.PerfilAcessoId = perfil.Id;
        contexto.Add(perfil);
        await contexto.SaveChangesAsync();

        var hoje = DateTimeOffset.UtcNow;
        var numeros = await CriarService(contexto).ObterMesEmNumerosAsync(cenario.UsuarioId, cenario.ContaId, hoje.Year, hoje.Month, CancellationToken.None);

        Assert.NotNull(numeros.SessoesRealizadas);
        Assert.Null(numeros.Recebido);
        Assert.Null(numeros.AReceber);
        Assert.Null(numeros.Vencido);
    }

    private static RelatoriosAdministrativosService CriarService(AppDbContext contexto) =>
        new(
            contexto,
            new AutorizacaoContaService(contexto),
            new AuditoriaCaptura(),
            new RelatorioHtmlRenderer(new HostAmbienteTeste()));

    private static async Task<Cenario> CriarCenarioAsync(AppDbContext contexto)
    {
        var contaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var paciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            NomeCompleto = "Paciente Relatorio",
            Status = StatusPaciente.Ativo
        };
        var tipoId = Guid.NewGuid();
        var prontuarioId = Guid.NewGuid();
        var consultaId = Guid.NewGuid();
        contexto.AddRange(
            new ContaPsicologo { Id = contaId, NomeConta = "Consultório Relatório", Status = StatusContaPsicologo.Ativa },
            new Usuario
            {
                Id = usuarioId,
                ContaPsicologoId = contaId,
                NomeCompleto = "Psicologa",
                Email = $"{usuarioId:N}@teste.local",
                SenhaHash = "hash",
                Perfil = PerfilUsuario.Psicologo,
                Status = StatusUsuario.Ativo
            },
            new DadosProfissionaisPsicologo
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                UsuarioId = usuarioId,
                Crp = "06/99999",
                CrpHash = "hash",
                NomeProfissional = "Dra. Relatório",
                Status = StatusUsuario.Ativo
            },
            paciente,
            new TipoAtendimento
            {
                Id = tipoId,
                ContaPsicologoId = contaId,
                Nome = "Sessao",
                DuracaoMinutos = 50,
                ModalidadePadrao = ModalidadeAtendimento.Presencial,
                Ativo = true
            },
            new Prontuario
            {
                Id = prontuarioId,
                ContaPsicologoId = contaId,
                PacienteId = paciente.Id,
                Status = StatusProntuario.Ativo
            },
            new Consulta
            {
                Id = consultaId,
                ContaPsicologoId = contaId,
                PacienteId = paciente.Id,
                TipoAtendimentoId = tipoId,
                InicioEm = DateTimeOffset.UtcNow.AddDays(-1),
                FimEm = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(50),
                Status = StatusConsulta.Realizada,
                Modalidade = ModalidadeAtendimento.Presencial
            });
        await contexto.SaveChangesAsync();
        return new Cenario(contaId, usuarioId, paciente, tipoId, prontuarioId, new Consulta { Id = consultaId });
    }

    private static AppDbContext CriarContexto() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record Cenario(
        Guid ContaId,
        Guid UsuarioId,
        Paciente Paciente,
        Guid TipoId,
        Guid ProntuarioId,
        Consulta Consulta);

    private sealed class AuditoriaCaptura : IAuditoriaService
    {
        public void Registrar(
            Guid contaPsicologoId,
            Guid? usuarioId,
            string tipoAcao,
            string entidade,
            Guid? entidadeId,
            string descricao,
            object? dadosAntes = null,
            object? dadosDepois = null,
            string? ip = null,
            string? userAgent = null)
        {
        }
    }

    private sealed class HostAmbienteTeste : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Psique.Api.Tests";
        public string ContentRootPath { get; set; } = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Psique.Api"));
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

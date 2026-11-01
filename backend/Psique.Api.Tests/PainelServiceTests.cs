using Psique.Api.Data;
using Psique.Api.Models;
using Psique.Api.Services.Painel;
using Psique.Api.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Psique.Api.Tests;

public sealed class PainelServiceTests
{
    [Fact]
    public async Task Painel_UsaDiaHoraESemanaDoConsultorioMesmoComDatasEmUtc()
    {
        await using var contexto = CriarContexto();
        var contaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var pacienteId = Guid.NewGuid();
        var tipoId = Guid.NewGuid();
        contexto.AddRange(
            new ContaPsicologo { Id = contaId },
            new Usuario { Id = usuarioId, ContaPsicologoId = contaId, NomeCompleto = "Ana Silva", Email = "ana@teste.local", SenhaHash = "hash", Perfil = PerfilUsuario.Psicologo },
            new ConfiguracaoAtendimento { Id = Guid.NewGuid(), ContaPsicologoId = contaId, FusoHorario = "America/Rio_Branco" },
            new Paciente { Id = pacienteId, ContaPsicologoId = contaId, NomeCompleto = "Paciente" },
            new TipoAtendimento { Id = tipoId, ContaPsicologoId = contaId, Nome = "Sessão" });
        foreach (var dia in new[] { 30, 31 })
        {
            var inicio = new DateTimeOffset(2026, 10, dia, 23, 30, 0, TimeSpan.FromHours(-5)).ToUniversalTime();
            contexto.Add(new Consulta { Id = Guid.NewGuid(), ContaPsicologoId = contaId, PacienteId = pacienteId, TipoAtendimentoId = tipoId,
                InicioEm = inicio, FimEm = inicio.AddMinutes(50), Status = StatusConsulta.Confirmada });
        }
        await contexto.SaveChangesAsync();
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 10, 31, 2, 0, 0, TimeSpan.Zero));
        var resultado = await new PainelService(contexto, relogio).ObterResumoAsync(usuarioId, contaId, CancellationToken.None);
        Assert.Equal("23:30", Assert.Single(resultado.ConsultasHoje).HorarioInicio);
        var amanha = resultado.VisaoSemana.First();
        Assert.Equal(new DateOnly(2026, 10, 31), amanha.Data);
        Assert.Equal("Amanhã", amanha.Rotulo);
        Assert.Equal(1, amanha.Consultas);
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    [Fact]
    public async Task ObterResumo_DeveRetornarSomenteDadosDaContaAutenticada()
    {
        await using var contexto = CriarContexto();
        var contaId = Guid.NewGuid();
        var outraContaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var hoje = DateTimeOffset.Now;

        var conta = new ContaPsicologo
        {
            Id = contaId,
            NomeConta = "Consultório Principal",
            Status = StatusContaPsicologo.Ativa
        };
        var outraConta = new ContaPsicologo
        {
            Id = outraContaId,
            NomeConta = "Outra Conta",
            Status = StatusContaPsicologo.Ativa
        };
        var usuario = new Usuario
        {
            Id = usuarioId,
            ContaPsicologoId = contaId,
            NomeCompleto = "Dra. Ana Souza",
            Email = "ana@psique.local",
            SenhaHash = "hash",
            Perfil = PerfilUsuario.Psicologo,
            Status = StatusUsuario.Ativo
        };
        var tipo = new TipoAtendimento
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            Nome = "Psicoterapia",
            DuracaoMinutos = 50,
            ModalidadePadrao = ModalidadeAtendimento.Online
        };
        var paciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            NomeCompleto = "Paciente Conta",
            Status = StatusPaciente.Ativo
        };
        var outroPaciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            NomeCompleto = "Paciente Outra Conta",
            Status = StatusPaciente.Ativo
        };

        contexto.AddRange(
            conta,
            outraConta,
            usuario,
            tipo,
            paciente,
            outroPaciente,
            new Consulta
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                PacienteId = paciente.Id,
                TipoAtendimentoId = tipo.Id,
                InicioEm = hoje.Date.AddHours(9),
                FimEm = hoje.Date.AddHours(10),
                Status = StatusConsulta.Confirmada,
                Modalidade = ModalidadeAtendimento.Online
            },
            new Consulta
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = outraContaId,
                PacienteId = outroPaciente.Id,
                TipoAtendimentoId = Guid.NewGuid(),
                InicioEm = hoje.Date.AddHours(11),
                FimEm = hoje.Date.AddHours(12),
                Status = StatusConsulta.Confirmada,
                Modalidade = ModalidadeAtendimento.Presencial
            },
            new Pagamento
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                PacienteId = paciente.Id,
                Valor = 250,
                Status = StatusPagamento.Pendente
            },
            new Pagamento
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = outraContaId,
                PacienteId = outroPaciente.Id,
                Valor = 999,
                Status = StatusPagamento.Pendente
            });
        await contexto.SaveChangesAsync();

        var service = new PainelService(contexto);

        var resumo = await service.ObterResumoAsync(
            usuarioId,
            contaId,
            CancellationToken.None);

        Assert.Equal("Ana", resumo.Usuario.PrimeiroNome);
        Assert.Equal(1, resumo.Indicadores.ConsultasHoje);
        Assert.Equal(1, resumo.Indicadores.PacientesAtivos);
        Assert.Equal(250, resumo.Indicadores.PagamentosPendentes);
        Assert.Single(resumo.ConsultasHoje);
        Assert.Equal("Paciente Conta", resumo.ConsultasHoje.Single().Paciente);
        Assert.Contains(
            resumo.Prioridades,
            prioridade => prioridade.Rota == "/pagamentos");
    }

    private static AppDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}

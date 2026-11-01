using Psique.Api.Configuracoes;
using Psique.Api.Data;
using Psique.Api.DTOs.Lembretes;
using Psique.Api.DTOs.ModelosMensagem;
using Psique.Api.Models;
using Psique.Api.Services.Comunicacao;
using Psique.Api.Services.Lembretes;
using Psique.Api.Services.ModelosMensagem;
using Psique.Api.Services.Notificacoes;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Psique.Api.Tests;

public sealed class LembretesServiceTests(BancoIntegridadeRelacional banco) : IClassFixture<BancoIntegridadeRelacional>
{
    [SqlServerFact]
    public async Task InterrupcaoSmtp_MarcadorPersisteEntreConexoesSqlIndependentes()
    {
        Guid id;
        var email = new EmailFake { InterromperAposAceite = true };
        await using (var antes = banco.CriarContexto())
        {
            var cenario = await CriarCenarioAsync(antes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => CriarLembretesService(antes, true, email).CriarAsync(
                cenario.UsuarioId, cenario.ContaId, new CriarLembreteRequest
                {
                    ConsultaId = cenario.Consulta.Id, Canal = CanalComunicacao.Email,
                    AgendadoPara = DateTimeOffset.UtcNow.AddMinutes(-1), Assunto = "Consulta", Mensagem = "Lembrete de teste"
                }, null, null, default));
            id = await antes.Lembretes.Select(l => l.Id).SingleAsync();
        }
        await using var depois = banco.CriarContexto();
        var aposReinicio = new EmailFake();
        await CriarLembretesService(depois, true, aposReinicio).ProcessarVencidosAsync(default);
        Assert.Single(email.Destinatarios);
        Assert.Empty(aposReinicio.Destinatarios);
        Assert.Equal(StatusLembrete.Falhou, (await depois.Lembretes.SingleAsync(l => l.Id == id)).Status);
    }

    [Fact]
    public async Task InterrupcaoDepoisDoAceiteSmtp_NaoEnviaOutraVezAoRetomar()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var email = new EmailFake { InterromperAposAceite = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CriarLembretesService(contexto, true, email).CriarAsync(
            cenario.UsuarioId, cenario.ContaId, new CriarLembreteRequest
            {
                ConsultaId = cenario.Consulta.Id, Canal = CanalComunicacao.Email,
                AgendadoPara = DateTimeOffset.UtcNow.AddMinutes(-1), Assunto = "Consulta", Mensagem = "Lembrete de teste"
            }, null, null, default));
        contexto.ChangeTracker.Clear();
        var aposReinicio = new EmailFake();
        var servico = CriarLembretesService(contexto, true, aposReinicio);
        await servico.ProcessarVencidosAsync(default);
        Assert.Single(email.Destinatarios);
        Assert.Empty(aposReinicio.Destinatarios);
        var lembrete = await contexto.Lembretes.SingleAsync();
        Assert.Equal(StatusLembrete.Falhou, lembrete.Status);
        Assert.Contains("Confira", lembrete.ErroEnvio);
        await Assert.ThrowsAsync<RegraNegocioException>(() => servico.TentarNovamenteAsync(
            cenario.UsuarioId, cenario.ContaId, lembrete.Id, null, null, default));
        Assert.Empty(aposReinicio.Destinatarios);
    }

    [Fact]
    public async Task Previa_UsaDataEHoraDoFusoDaConta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        (await contexto.ConfiguracoesAtendimento.SingleAsync()).FusoHorario = "America/Rio_Branco";
        cenario.Consulta.InicioEm = new DateTimeOffset(2026, 11, 2, 23, 30, 0, TimeSpan.FromHours(-5)).ToUniversalTime();
        await contexto.SaveChangesAsync();
        var request = new PreviaLembreteRequest { ConsultaId = cenario.Consulta.Id, Mensagem = "{{data_consulta}} {{horario_consulta}}" };
        var previa = await CriarLembretesService(contexto).PreviaAsync(cenario.UsuarioId, cenario.ContaId, request, CancellationToken.None);
        Assert.Equal("02/11/2026 23:30", previa.Mensagem);
        var modelo = await CriarModelosService(contexto).PreviaAsync(cenario.UsuarioId, cenario.ContaId,
            new PreviaModeloMensagemRequest { ConsultaId = cenario.Consulta.Id, Conteudo = request.Mensagem }, CancellationToken.None);
        Assert.Equal("02/11/2026 23:30", modelo.Conteudo);
    }

    [Fact]
    public async Task EnviarEmail_DecifraEnderecoDoPacienteAntesDoTransporte()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        cenario.Paciente.Email = "protegido:paciente@teste.local";
        await contexto.SaveChangesAsync();
        var email = new EmailFake();
        var resultado = await CriarLembretesService(contexto, emailAtivo: true, email: email).CriarAsync(
            cenario.UsuarioId, cenario.ContaId, new() { ConsultaId = cenario.Consulta.Id, Canal = CanalComunicacao.Email,
                AgendadoPara = DateTimeOffset.UtcNow.AddSeconds(-1), Assunto = "Consulta", Mensagem = "Lembrete" }, null, null, CancellationToken.None);
        Assert.Equal("paciente@teste.local", Assert.Single(email.Destinatarios));
        Assert.Equal(StatusLembrete.Enviado, resultado.Status);
    }

    [Theory]
    [InlineData(StatusConsulta.Cancelada)]
    [InlineData(StatusConsulta.Remarcada)]
    [InlineData(StatusConsulta.Realizada)]
    [InlineData(StatusConsulta.NaoCompareceu)]
    public async Task Reenviar_NaoEnviaLembreteDeConsultaEncerrada(StatusConsulta status)
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        cenario.Consulta.Status = status;
        var lembrete = AdicionarLembrete(contexto, cenario, StatusLembrete.Falhou);
        await contexto.SaveChangesAsync();
        var email = new EmailFake();
        var resultado = await CriarLembretesService(contexto, true, email).TentarNovamenteAsync(cenario.UsuarioId, cenario.ContaId, lembrete.Id, null, null, CancellationToken.None);
        Assert.Empty(email.Destinatarios);
        Assert.Equal(StatusLembrete.Ignorado, resultado.Status);
    }

    [Fact]
    public async Task Reenviar_NaoEnviaLembreteDepoisDoInicioDaConsulta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        cenario.Consulta.InicioEm = DateTimeOffset.UtcNow.AddHours(-1);
        var lembrete = AdicionarLembrete(contexto, cenario, StatusLembrete.Falhou);
        await contexto.SaveChangesAsync();
        var email = new EmailFake();
        var resultado = await CriarLembretesService(contexto, true, email).TentarNovamenteAsync(cenario.UsuarioId, cenario.ContaId, lembrete.Id, null, null, CancellationToken.None);
        Assert.Empty(email.Destinatarios);
        Assert.Equal(StatusLembrete.Ignorado, resultado.Status);
    }

    [Fact]
    public async Task Criar_RecusaLembreteAgendadoParaDepoisDaConsulta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        await Assert.ThrowsAsync<RegraNegocioException>(() => CriarLembretesService(contexto).CriarAsync(cenario.UsuarioId, cenario.ContaId,
            new() { ConsultaId = cenario.Consulta.Id, Canal = CanalComunicacao.Email, AgendadoPara = cenario.Consulta.InicioEm.AddMinutes(1), Assunto = "Consulta", Mensagem = "Lembrete" }, null, null, CancellationToken.None));
        Assert.Empty(contexto.Lembretes);
    }

    [Theory]
    [InlineData(StatusContaPsicologo.Inativa)]
    [InlineData(StatusContaPsicologo.Bloqueada)]
    [InlineData(StatusContaPsicologo.Cancelada)]
    public async Task ProcessarVencidos_NaoEnviaParaContaDesativada(StatusContaPsicologo status)
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        (await contexto.ContasPsicologo.SingleAsync()).Status = status;
        AdicionarLembrete(contexto, cenario, StatusLembrete.Agendado);
        await contexto.SaveChangesAsync();
        var email = new EmailFake();
        await CriarLembretesService(contexto, true, email).ProcessarVencidosAsync(CancellationToken.None);
        Assert.Empty(email.Destinatarios);
    }

    private static Lembrete AdicionarLembrete(AppDbContext contexto, Cenario cenario, StatusLembrete status)
    {
        var lembrete = new Lembrete { Id = Guid.NewGuid(), ContaPsicologoId = cenario.ContaId, ConsultaId = cenario.Consulta.Id,
            PacienteId = cenario.Paciente.Id, Canal = CanalComunicacao.Email, Tipo = TipoLembrete.LembreteConsulta, Status = status,
            AgendadoPara = DateTimeOffset.UtcNow.AddMinutes(-1), Assunto = "Consulta", Mensagem = "Lembrete" };
        contexto.Add(lembrete);
        return lembrete;
    }

    [Fact]
    public async Task ProcessarVencidos_EncerraLembreteDeConsultaQueJaComecou()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        cenario.Consulta.InicioEm = DateTimeOffset.UtcNow.AddHours(-2);
        cenario.Consulta.FimEm = DateTimeOffset.UtcNow.AddHours(-1);
        var lembrete = new Lembrete
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = cenario.ContaId,
            ConsultaId = cenario.Consulta.Id,
            PacienteId = cenario.Paciente.Id,
            Canal = CanalComunicacao.Email,
            Tipo = TipoLembrete.LembreteConsulta,
            Status = StatusLembrete.Agendado,
            AgendadoPara = DateTimeOffset.UtcNow.AddDays(-1),
            Mensagem = "Atrasado"
        };
        contexto.Add(lembrete);
        await contexto.SaveChangesAsync();

        var processados = await CriarLembretesService(contexto).ProcessarVencidosAsync(CancellationToken.None);

        Assert.Equal(1, processados);
        var salvo = await contexto.Lembretes.SingleAsync(x => x.Id == lembrete.Id);
        Assert.Equal(StatusLembrete.Ignorado, salvo.Status);
    }

    [Fact]
    public async Task ProcessarVencidos_NaoRepegaLembreteJaTentadoNoMesmoCiclo()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        contexto.Add(new Lembrete
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = cenario.ContaId,
            ConsultaId = cenario.Consulta.Id,
            PacienteId = cenario.Paciente.Id,
            Canal = CanalComunicacao.Sistema,
            Tipo = TipoLembrete.LembreteConsulta,
            Status = StatusLembrete.Agendado,
            AgendadoPara = DateTimeOffset.UtcNow.AddMinutes(-5),
            Mensagem = "Interno"
        });
        await contexto.SaveChangesAsync();
        var service = CriarLembretesService(contexto);

        Assert.Equal(1, await service.ProcessarVencidosAsync(CancellationToken.None));
        Assert.Equal(0, await service.ProcessarVencidosAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Listar_DeveIsolarLembretesPorConta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var outraContaId = Guid.NewGuid();
        var outroPaciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            NomeCompleto = "Externo",
            Status = StatusPaciente.Ativo
        };
        var outroTipo = new TipoAtendimento
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            Nome = "Externo",
            DuracaoMinutos = 50,
            ModalidadePadrao = ModalidadeAtendimento.Online,
            Ativo = true
        };
        var outraConsulta = new Consulta
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            PacienteId = outroPaciente.Id,
            TipoAtendimentoId = outroTipo.Id,
            InicioEm = DateTimeOffset.UtcNow.AddDays(1),
            FimEm = DateTimeOffset.UtcNow.AddDays(1).AddMinutes(50),
            Status = StatusConsulta.Agendada,
            Modalidade = ModalidadeAtendimento.Online
        };

        contexto.AddRange(
            new ContaPsicologo { Id = outraContaId, NomeConta = "Outra", Status = StatusContaPsicologo.Ativa },
            outroPaciente,
            outroTipo,
            outraConsulta,
            new Lembrete
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = cenario.ContaId,
                ConsultaId = cenario.Consulta.Id,
                PacienteId = cenario.Paciente.Id,
                Canal = CanalComunicacao.Email,
                Tipo = TipoLembrete.LembreteConsulta,
                Status = StatusLembrete.Agendado,
                AgendadoPara = DateTimeOffset.UtcNow.AddHours(2),
                Mensagem = "Da conta"
            },
            new Lembrete
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = outraContaId,
                ConsultaId = outraConsulta.Id,
                PacienteId = outroPaciente.Id,
                Canal = CanalComunicacao.Email,
                Tipo = TipoLembrete.LembreteConsulta,
                Status = StatusLembrete.Agendado,
                AgendadoPara = DateTimeOffset.UtcNow.AddHours(2),
                Mensagem = "Outra conta"
            });
        await contexto.SaveChangesAsync();

        var service = CriarLembretesService(contexto);
        var resultado = await service.ListarAsync(cenario.UsuarioId, cenario.ContaId, new(), CancellationToken.None);

        Assert.Single(resultado.Itens);
        Assert.Equal("Da conta", resultado.Itens.Single().Mensagem);
    }

    [Fact]
    public async Task Criar_NaoDeveAceitarConsultaDeOutraConta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var outraContaId = Guid.NewGuid();
        var outroPaciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            NomeCompleto = "Externo",
            Status = StatusPaciente.Ativo
        };
        var outroTipo = new TipoAtendimento
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            Nome = "Externo",
            DuracaoMinutos = 50,
            ModalidadePadrao = ModalidadeAtendimento.Online,
            Ativo = true
        };
        var outraConsulta = new Consulta
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            PacienteId = outroPaciente.Id,
            TipoAtendimentoId = outroTipo.Id,
            InicioEm = DateTimeOffset.UtcNow.AddDays(1),
            FimEm = DateTimeOffset.UtcNow.AddDays(1).AddMinutes(50),
            Status = StatusConsulta.Agendada,
            Modalidade = ModalidadeAtendimento.Online
        };
        contexto.AddRange(
            new ContaPsicologo { Id = outraContaId, NomeConta = "Outra", Status = StatusContaPsicologo.Ativa },
            outroPaciente,
            outroTipo,
            outraConsulta);
        await contexto.SaveChangesAsync();

        var service = CriarLembretesService(contexto);
        var excecao = await Assert.ThrowsAsync<RegraNegocioException>(() => service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarLembreteRequest
            {
                ConsultaId = outraConsulta.Id,
                Canal = CanalComunicacao.Email,
                Tipo = TipoLembrete.LembreteConsulta,
                AgendadoPara = DateTimeOffset.UtcNow.AddHours(6),
                Assunto = "Lembrete",
                Mensagem = "Olá, {{nome_paciente}}"
            },
            null,
            null,
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, excecao.StatusCode);
        Assert.Empty(contexto.Lembretes);
    }

    [Fact]
    public async Task Criar_NaoDeveAceitarModeloDeOutraConta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var outraContaId = Guid.NewGuid();
        var modeloExterno = new ModeloMensagem
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            Nome = "Externo",
            Tipo = TipoLembrete.LembreteConsulta,
            Canal = CanalComunicacao.Email,
            Assunto = "Assunto",
            Conteudo = "Olá, {{nome_paciente}}",
            Ativo = true
        };
        contexto.AddRange(
            new ContaPsicologo { Id = outraContaId, NomeConta = "Outra", Status = StatusContaPsicologo.Ativa },
            modeloExterno);
        await contexto.SaveChangesAsync();

        var service = CriarLembretesService(contexto);
        var excecao = await Assert.ThrowsAsync<RegraNegocioException>(() => service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarLembreteRequest
            {
                ConsultaId = cenario.Consulta.Id,
                ModeloMensagemId = modeloExterno.Id,
                Canal = CanalComunicacao.Email,
                Tipo = TipoLembrete.LembreteConsulta,
                AgendadoPara = DateTimeOffset.UtcNow.AddHours(6)
            },
            null,
            null,
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, excecao.StatusCode);
    }

    [Fact]
    public async Task Criar_DeveImpedirDuplicidadeDeLembreteProgramado()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var service = CriarLembretesService(contexto);
        var request = new CriarLembreteRequest
        {
            ConsultaId = cenario.Consulta.Id,
            Canal = CanalComunicacao.Email,
            Tipo = TipoLembrete.LembreteConsulta,
            AgendadoPara = DateTimeOffset.UtcNow.AddHours(12),
            Assunto = "Lembrete",
            Mensagem = "Olá, {{nome_paciente}}"
        };

        await service.CriarAsync(cenario.UsuarioId, cenario.ContaId, request, null, null, CancellationToken.None);
        var excecao = await Assert.ThrowsAsync<RegraNegocioException>(() =>
            service.CriarAsync(cenario.UsuarioId, cenario.ContaId, request, null, null, CancellationToken.None));

        Assert.Contains("Já existe um lembrete programado", excecao.Message);
        Assert.Single(contexto.Lembretes);
    }

    [Fact]
    public async Task Cancelar_DeveRejeitarTransicaoInvalidaDeEnviado()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var lembrete = new Lembrete
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = cenario.ContaId,
            ConsultaId = cenario.Consulta.Id,
            PacienteId = cenario.Paciente.Id,
            Canal = CanalComunicacao.Email,
            Tipo = TipoLembrete.LembreteConsulta,
            Status = StatusLembrete.Enviado,
            AgendadoPara = DateTimeOffset.UtcNow.AddHours(-1),
            EnviadoEm = DateTimeOffset.UtcNow,
            Mensagem = "Enviado"
        };
        contexto.Lembretes.Add(lembrete);
        await contexto.SaveChangesAsync();

        var service = CriarLembretesService(contexto);
        var excecao = await Assert.ThrowsAsync<RegraNegocioException>(() =>
            service.CancelarAsync(cenario.UsuarioId, cenario.ContaId, lembrete.Id, null, null, CancellationToken.None));

        Assert.Contains("programados ou com falha", excecao.Message);
        Assert.Equal(StatusLembrete.Enviado, lembrete.Status);
    }

    [Fact]
    public async Task Criar_ComEmailDesabilitado_DeveManterProgramadoSemFingirEnvio()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var service = CriarLembretesService(contexto, emailAtivo: false);

        var resultado = await service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarLembreteRequest
            {
                ConsultaId = cenario.Consulta.Id,
                Canal = CanalComunicacao.Email,
                Tipo = TipoLembrete.LembreteConsulta,
                AgendadoPara = DateTimeOffset.UtcNow,
                Assunto = "Lembrete",
                Mensagem = "Olá, {{nome_paciente}}"
            },
            null,
            null,
            CancellationToken.None);

        Assert.Equal(StatusLembrete.Agendado, resultado.Status);
        Assert.Null(resultado.EnviadoEm);
        Assert.Contains("desabilitado", resultado.ErroEnvio ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Modelo_NaoDeveAceitarPacienteDeOutraContaNaPrevia()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var outraContaId = Guid.NewGuid();
        var outroPaciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = outraContaId,
            NomeCompleto = "Externo",
            Status = StatusPaciente.Ativo
        };
        contexto.AddRange(
            new ContaPsicologo { Id = outraContaId, NomeConta = "Outra", Status = StatusContaPsicologo.Ativa },
            outroPaciente);
        await contexto.SaveChangesAsync();

        var service = CriarModelosService(contexto);
        var excecao = await Assert.ThrowsAsync<RegraNegocioException>(() => service.PreviaAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new PreviaModeloMensagemRequest
            {
                Conteudo = "Olá, {{nome_paciente}}",
                PacienteId = outroPaciente.Id
            },
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, excecao.StatusCode);
    }

    [Fact]
    public async Task CriarModelo_DeveRegistrarAuditoria()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var auditoria = new AuditoriaCaptura();
        var service = new ModelosMensagemService(
            contexto,
            new AutorizacaoContaService(contexto),
            auditoria,
            new ModeloMensagemTemplateService());

        await service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarModeloMensagemRequest
            {
                Nome = "Lembrete",
                Tipo = TipoLembrete.LembreteConsulta,
                Canal = CanalComunicacao.Email,
                Assunto = "Lembrete",
                Conteudo = "Olá, {{nome_paciente}}",
                Ativo = true
            },
            "127.0.0.1",
            "teste",
            CancellationToken.None);

        Assert.Contains(auditoria.Registros, item => item.TipoAcao == "ModeloMensagemCriado");
    }

    [Theory]
    [InlineData(TipoLembrete.ConfirmacaoConsulta, CanalComunicacao.Email)]
    [InlineData(TipoLembrete.LembreteConsulta, CanalComunicacao.Sistema)]
    [InlineData(TipoLembrete.LembreteConsulta, CanalComunicacao.Sms)]
    public async Task CriarModelo_ParaEnvioQueNaoExiste_DeveBloquear(TipoLembrete tipo, CanalComunicacao canal)
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var service = new ModelosMensagemService(
            contexto,
            new AutorizacaoContaService(contexto),
            new AuditoriaCaptura(),
            new ModeloMensagemTemplateService());

        await Assert.ThrowsAsync<RegraNegocioException>(() => service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarModeloMensagemRequest
            {
                Nome = "Modelo sem envio",
                Tipo = tipo,
                Canal = canal,
                Assunto = "Assunto",
                Conteudo = "Olá, {{nome_paciente}}",
                Ativo = true
            },
            null,
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task Recepcionista_PodeGerenciarLembretesAdministrativos()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto, PerfilUsuario.Recepcionista);
        var service = CriarLembretesService(contexto);

        var resultado = await service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarLembreteRequest
            {
                ConsultaId = cenario.Consulta.Id,
                Canal = CanalComunicacao.Email,
                Tipo = TipoLembrete.LembreteConsulta,
                AgendadoPara = DateTimeOffset.UtcNow.AddHours(3),
                Assunto = "Lembrete de consulta",
                Mensagem = "Olá, {{nome_paciente}}. Consulta em {{data_consulta}}."
            },
            null,
            null,
            CancellationToken.None);

        Assert.Equal(StatusLembrete.Agendado, resultado.Status);
        Assert.Contains("Olá, Paciente da conta", resultado.Mensagem);
    }

    [Fact]
    public async Task ProgramarAutomatico_DeveCriarLembreteComAntecedencia()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var service = CriarLembretesService(contexto);

        var resultado = await service.ProgramarAutomaticoParaConsultaAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            cenario.Consulta.Id,
            null,
            null,
            CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(StatusLembrete.Agendado, resultado!.Status);
        Assert.Equal(TipoLembrete.LembreteConsulta, resultado.Tipo);
        Assert.True(resultado.AgendadoPara <= cenario.Consulta.InicioEm);
        Assert.Contains("Paciente da conta", resultado.Mensagem);
    }

    [Fact]
    public async Task EncerrarProgramados_DeveIgnorarLembretesDaConsulta()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var service = CriarLembretesService(contexto);
        await service.ProgramarAutomaticoParaConsultaAsync(
            cenario.UsuarioId, cenario.ContaId, cenario.Consulta.Id, null, null, CancellationToken.None);

        var quantidade = await service.EncerrarProgramadosDaConsultaAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            cenario.Consulta.Id,
            StatusLembrete.Ignorado,
            "Consulta cancelada",
            null,
            null,
            CancellationToken.None);

        Assert.Equal(1, quantidade);
        Assert.All(contexto.Lembretes, item => Assert.Equal(StatusLembrete.Ignorado, item.Status));
    }

    [Fact]
    public async Task ProcessarVencidos_ComConsultaCancelada_DeveIgnorar()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        cenario.Consulta.Status = StatusConsulta.Cancelada;
        var lembrete = new Lembrete
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = cenario.ContaId,
            ConsultaId = cenario.Consulta.Id,
            PacienteId = cenario.Paciente.Id,
            Canal = CanalComunicacao.Email,
            Tipo = TipoLembrete.LembreteConsulta,
            Status = StatusLembrete.Agendado,
            AgendadoPara = DateTimeOffset.UtcNow.AddMinutes(-10),
            Assunto = "Assunto",
            Mensagem = "Mensagem"
        };
        contexto.Lembretes.Add(lembrete);
        await contexto.SaveChangesAsync();

        var service = CriarLembretesService(contexto);
        var processados = await service.ProcessarVencidosAsync(CancellationToken.None);

        Assert.Equal(1, processados);
        Assert.Equal(StatusLembrete.Ignorado, lembrete.Status);
    }

    [Fact]
    public async Task Criar_WhatsAppComConversa_DeveRegistrarMensagemDeSaida()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        var integracaoId = Guid.NewGuid();
        var conversaId = Guid.NewGuid();
        contexto.AddRange(
            new IntegracaoWhatsApp
            {
                Id = integracaoId,
                ContaPsicologoId = cenario.ContaId,
                Nome = "Bridge",
                Provedor = "Bridge",
                UrlBaseBridge = "https://bridge.local",
                TokenCriptografado = "x",
                TokenHash = "y",
                NumeroWhatsApp = "5511888888888",
                Status = StatusIntegracao.Ativa,
                Ativo = true
            },
            new ConversaWhatsApp
            {
                Id = conversaId,
                ContaPsicologoId = cenario.ContaId,
                IntegracaoWhatsAppId = integracaoId,
                PacienteId = cenario.Paciente.Id,
                Telefone = "5511999999999",
                Status = StatusConversaWhatsApp.Aberta
            });
        await contexto.SaveChangesAsync();

        var service = CriarLembretesService(contexto);
        var resposta = await service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarLembreteRequest
            {
                ConsultaId = cenario.Consulta.Id,
                Canal = CanalComunicacao.WhatsApp,
                AgendadoPara = DateTimeOffset.UtcNow.AddMinutes(-1),
                Mensagem = "Lembrete: consulta amanha as 10h."
            },
            null,
            null,
            CancellationToken.None);

        Assert.Equal(StatusLembrete.Agendado, resposta.Status);
        Assert.Null(resposta.EnviadoEm);
        var saida = Assert.Single(contexto.MensagensWhatsApp);
        Assert.Equal(DirecaoMensagem.Saida, saida.Direcao);
        Assert.Equal(conversaId, saida.ConversaWhatsAppId);
        Assert.Equal(resposta.Id, saida.LembreteId);
        Assert.Equal(StatusMensagemWhatsApp.Pendente, saida.Status);
    }

    [Fact]
    public async Task Criar_WhatsAppSemConversa_DeveManterProgramado()
    {
        await using var contexto = CriarContexto();
        var cenario = await CriarCenarioAsync(contexto);
        contexto.Add(
            new IntegracaoWhatsApp
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = cenario.ContaId,
                Nome = "Bridge",
                Provedor = "Bridge",
                UrlBaseBridge = "https://bridge.local",
                TokenCriptografado = "x",
                TokenHash = "y",
                NumeroWhatsApp = "5511888888888",
                Status = StatusIntegracao.Ativa,
                Ativo = true
            });
        await contexto.SaveChangesAsync();

        var service = CriarLembretesService(contexto);
        var resposta = await service.CriarAsync(
            cenario.UsuarioId,
            cenario.ContaId,
            new CriarLembreteRequest
            {
                ConsultaId = cenario.Consulta.Id,
                Canal = CanalComunicacao.WhatsApp,
                AgendadoPara = DateTimeOffset.UtcNow.AddMinutes(-1),
                Mensagem = "Lembrete: consulta amanha as 10h."
            },
            null,
            null,
            CancellationToken.None);

        Assert.Equal(StatusLembrete.Agendado, resposta.Status);
        Assert.Empty(contexto.MensagensWhatsApp);
    }

    private static LembretesService CriarLembretesService(AppDbContext contexto, bool emailAtivo = false, EmailFake? email = null) =>
        new(
            contexto,
            new AutorizacaoContaService(contexto),
            new AuditoriaCaptura(),
            new ModeloMensagemTemplateService(),
            email ?? new EmailFake(),
            Options.Create(new EmailConfiguracao { Ativo = emailAtivo }),
            new NotificacoesService(contexto, new AutorizacaoContaService(contexto)),
            new ProtecaoFake());

    private static ModelosMensagemService CriarModelosService(AppDbContext contexto) =>
        new(contexto, new AutorizacaoContaService(contexto), new AuditoriaCaptura(), new ModeloMensagemTemplateService());

    private static async Task<Cenario> CriarCenarioAsync(AppDbContext contexto, PerfilUsuario perfil = PerfilUsuario.Psicologo)
    {
        var contaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var paciente = new Paciente
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            NomeCompleto = "Paciente da conta",
            Email = "paciente@teste.local",
            Status = StatusPaciente.Ativo
        };
        var tipo = new TipoAtendimento
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            Nome = "Psicoterapia",
            DuracaoMinutos = 50,
            ModalidadePadrao = ModalidadeAtendimento.Presencial,
            Ativo = true
        };
        var consulta = new Consulta
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            PacienteId = paciente.Id,
            TipoAtendimentoId = tipo.Id,
            InicioEm = DateTimeOffset.UtcNow.AddDays(2),
            FimEm = DateTimeOffset.UtcNow.AddDays(2).AddMinutes(50),
            Status = StatusConsulta.Agendada,
            Modalidade = ModalidadeAtendimento.Presencial,
            EnderecoAtendimento = "Rua do Consultório, 10"
        };

        contexto.AddRange(
            new ContaPsicologo { Id = contaId, NomeConta = "Consultorio", Status = StatusContaPsicologo.Ativa },
            new Usuario
            {
                Id = usuarioId,
                ContaPsicologoId = contaId,
                NomeCompleto = "Profissional",
                Email = $"{usuarioId:N}@teste.local",
                SenhaHash = "hash",
                Perfil = perfil,
                Status = StatusUsuario.Ativo
            },
            new DadosProfissionaisPsicologo
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                UsuarioId = usuarioId,
                Crp = "06/12345",
                CrpHash = "hash",
                NomeProfissional = "Dra. Teste",
                Status = StatusUsuario.Ativo
            },
            new ConfiguracaoAtendimento
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                AntecedenciaCancelamentoHoras = 24,
                AntecedenciaRemarcacaoHoras = 12,
                ExigeJustificativaCancelamento = true,
                LembretesAutomaticosAtivos = true,
                ProgramarLembreteAoCriarConsulta = true,
                ProgramarLembreteAoConfirmar = true,
                AntecedenciaLembreteHoras = 24,
                CanalLembretePadrao = CanalComunicacao.Email,
                Ativo = true
            },
            paciente,
            tipo,
            consulta);
        await contexto.SaveChangesAsync();
        return new Cenario(contaId, usuarioId, paciente, consulta);
    }

    private static AppDbContext CriarContexto() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record Cenario(Guid ContaId, Guid UsuarioId, Paciente Paciente, Consulta Consulta);

    private sealed class AuditoriaCaptura : IAuditoriaService
    {
        public List<(string TipoAcao, string Entidade, Guid? EntidadeId)> Registros { get; } = [];

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
            string? userAgent = null) =>
            Registros.Add((tipoAcao, entidade, entidadeId));
    }

    private sealed class EmailFake : IEmailTransacionalService
    {
        public bool InterromperAposAceite { get; init; }
        public List<string> Destinatarios { get; } = [];
        public Task<bool> EnviarRecuperacaoSenhaAsync(string destinatario, string nome, string token, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> EnviarMensagemAdministrativaAsync(
            string destinatario,
            string nomeDestinatario,
            string assunto,
            string corpo,
            CancellationToken cancellationToken)
        {
            Destinatarios.Add(destinatario);
            if (InterromperAposAceite) throw new InvalidOperationException("Interrupção simulada após o aceite SMTP.");
            return Task.FromResult(true);
        }
    }

    private sealed class ProtecaoFake : IProtecaoDadosService
    {
        public string Proteger(string valor) => $"protegido:{valor}";
        public string Desproteger(string valorProtegido) =>
            valorProtegido.StartsWith("protegido:", StringComparison.Ordinal)
                ? valorProtegido["protegido:".Length..]
                : valorProtegido;
        public string HashDeterministico(string valor) => $"hash:{valor}";
        public string HashToken(string token) => $"tokenhash:{token}";
        public string GerarTokenSeguro() => "token";
    }
}

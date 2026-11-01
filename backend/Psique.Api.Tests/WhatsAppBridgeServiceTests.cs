using Psique.Api.Data;
using Psique.Api.DTOs.WhatsApp;
using Psique.Api.Models;
using Psique.Api.Services.Notificacoes;
using Psique.Api.Services.Seguranca;
using Psique.Api.Services.WhatsApp;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Psique.Api.Tests;

public sealed class WhatsAppBridgeServiceTests
{
    [Theory]
    [InlineData("mensagem.entregue")]
    [InlineData("mensagem.lida")]
    [InlineData("mensagem.recebida")]
    public async Task EventoAtrasado_NaoReativaConexaoDesconectada(string tipo)
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var cenario = await SemearContaAsync(contexto, protecao, false);
        var integracao = await contexto.IntegracoesWhatsApp.SingleAsync();
        integracao.Provedor = "Evolution API";
        integracao.Status = StatusIntegracao.Pendente;
        await contexto.SaveChangesAsync();
        await CriarBridge(contexto, protecao).ReceberEventoAsync(cenario.Token, new()
        {
            EventoExternoId = "antigo", MensagemExternaId = "msg-antiga", TipoEvento = tipo,
            Telefone = "5511999999999", Conteudo = "Olá"
        }, null, null, default);
        Assert.Equal(StatusIntegracao.Pendente, integracao.Status);
    }

    [Theory]
    [InlineData("1133334444")]
    [InlineData("11933334444")]
    [InlineData("551133334444")]
    [InlineData("5511933334444")]
    public async Task NovoContato_AceitaVariacoesDeTelefoneSemFalha(string telefone)
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var cenario = await SemearContaAsync(contexto, protecao, false);
        var resposta = await CriarBridge(contexto, protecao).ReceberEventoAsync(cenario.Token, new()
        {
            EventoExternoId = "telefone", MensagemExternaId = "msg-telefone", TipoEvento = "mensagem.recebida",
            Telefone = telefone, Conteudo = "Olá"
        }, null, null, default);
        Assert.True(resposta.Processado);
        Assert.Equal(telefone, Assert.Single(contexto.ConversasWhatsApp).Telefone);
    }

    [Theory]
    [InlineData(StatusContaPsicologo.Inativa)]
    [InlineData(StatusContaPsicologo.Bloqueada)]
    [InlineData(StatusContaPsicologo.Cancelada)]
    public async Task ReceberEvento_RecusaContaDesativadaMesmoComTokenValido(StatusContaPsicologo status)
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var cenario = await SemearContaAsync(contexto, protecao, false);
        (await contexto.ContasPsicologo.SingleAsync()).Status = status;
        await contexto.SaveChangesAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarBridge(contexto, protecao).ReceberEventoAsync(cenario.Token, Mensagem("conta-inativa", "Olá"), null, null, CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, erro.StatusCode);
        Assert.Empty(contexto.EventosBridgeWhatsApp);
        Assert.Empty(contexto.MensagensWhatsApp);
    }

    [Fact]
    public async Task ReceberEvento_SemToken_DeveNegar()
    {
        await using var contexto = CriarContexto();
        var service = CriarBridge(contexto);

        var excecao = await Assert.ThrowsAsync<RegraNegocioException>(() =>
            service.ReceberEventoAsync(
                "",
                new ReceberEventoBridgeRequest
                {
                    TipoEvento = "mensagem.recebida",
                    Telefone = "5511999999999",
                    Conteudo = "Quero agendar"
                },
                null,
                null,
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, excecao.StatusCode);
    }

    [Fact]
    public async Task ReceberMensagem_DeveCriarConversaEMensagemProtegida()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var token = "token-bridge-super-seguro";
        var contaId = Guid.NewGuid();
        contexto.AddRange(
            new ContaPsicologo { Id = contaId, NomeConta = "Conta WA", Status = StatusContaPsicologo.Ativa },
            new IntegracaoWhatsApp
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                Nome = "Bridge",
                Provedor = "Bridge",
                UrlBaseBridge = "https://bridge.local",
                TokenCriptografado = protecao.Proteger(token),
                TokenHash = protecao.HashToken(token),
                NumeroWhatsApp = "5511888888888",
                Status = StatusIntegracao.Ativa,
                Ativo = true
            },
            new Usuario
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                NomeCompleto = "Psi",
                Email = "psi@teste.local",
                SenhaHash = "x",
                Perfil = PerfilUsuario.Psicologo,
                Status = StatusUsuario.Ativo
            });
        await contexto.SaveChangesAsync();

        var service = CriarBridge(contexto, protecao);
        var resultado = await service.ReceberEventoAsync(
            token,
            new ReceberEventoBridgeRequest
            {
                EventoExternoId = "evt-1",
                TipoEvento = "mensagem.recebida",
                Telefone = "5511999999999",
                NomeContato = "Maria",
                Conteudo = "Quero remarcar minha consulta",
                MensagemExternaId = "msg-1"
            },
            null,
            null,
            CancellationToken.None);

        Assert.True(resultado.Processado);
        Assert.False(resultado.Duplicado);
        Assert.NotNull(resultado.ConversaId);
        Assert.Single(contexto.ConversasWhatsApp);
        Assert.Equal(2, contexto.MensagensWhatsApp.Count());
        Assert.Contains(contexto.MensagensWhatsApp, m => m.Direcao == DirecaoMensagem.Entrada && m.Conteudo!.StartsWith("protegido:"));
        Assert.Contains(contexto.MensagensWhatsApp, m => m.Direcao == DirecaoMensagem.Saida);
        Assert.Contains(contexto.SessoesBot, s => s.IntencaoAtual == "remarcar_consulta");
    }

    [Fact]
    public async Task ReceberMensagem_DeveVincularPacientePeloHashDoContato()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var token = "token-bridge-vinculo-01";
        var contaId = Guid.NewGuid();
        var pacienteId = Guid.NewGuid();
        const string telefone = "5511999999999";
        contexto.AddRange(
            new ContaPsicologo { Id = contaId, NomeConta = "Conta WA", Status = StatusContaPsicologo.Ativa },
            new IntegracaoWhatsApp
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                Nome = "Bridge",
                Provedor = "Bridge",
                UrlBaseBridge = "https://bridge.local",
                TokenCriptografado = protecao.Proteger(token),
                TokenHash = protecao.HashToken(token),
                NumeroWhatsApp = "5511888888888",
                Status = StatusIntegracao.Ativa,
                Ativo = true
            },
            new Paciente
            {
                Id = pacienteId,
                ContaPsicologoId = contaId,
                NomeCompleto = "Paciente Psique",
                Telefone = protecao.Proteger(telefone),
                Status = StatusPaciente.Ativo
            },
            new ContatoPaciente
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                PacienteId = pacienteId,
                TipoContato = TipoContato.WhatsApp,
                Valor = protecao.Proteger(telefone),
                ValorHash = protecao.HashDeterministico(telefone),
                Principal = true
            });
        await contexto.SaveChangesAsync();

        var service = CriarBridge(contexto, protecao);
        var resultado = await service.ReceberEventoAsync(
            token,
            new ReceberEventoBridgeRequest
            {
                EventoExternoId = "evt-vinc",
                TipoEvento = "mensagem.recebida",
                Telefone = telefone,
                Conteudo = "ola, quais horários?",
                MensagemExternaId = "msg-vinc"
            },
            null,
            null,
            CancellationToken.None);

        Assert.True(resultado.Processado);
        var conversa = Assert.Single(contexto.ConversasWhatsApp);
        Assert.Equal(pacienteId, conversa.PacienteId);
    }

    [Fact]
    public async Task ConteudoSensivel_DeveExigirHumanoENotificar()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var token = "token-bridge-sensivel-01";
        var contaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        contexto.AddRange(
            new ContaPsicologo { Id = contaId, NomeConta = "Conta WA", Status = StatusContaPsicologo.Ativa },
            new IntegracaoWhatsApp
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                Nome = "Bridge",
                Provedor = "Bridge",
                UrlBaseBridge = "https://bridge.local",
                TokenCriptografado = protecao.Proteger(token),
                TokenHash = protecao.HashToken(token),
                NumeroWhatsApp = "5511888888888",
                Status = StatusIntegracao.Ativa,
                Ativo = true
            },
            new Usuario
            {
                Id = usuarioId,
                ContaPsicologoId = contaId,
                NomeCompleto = "Psi",
                Email = "psi@teste.local",
                SenhaHash = "x",
                Perfil = PerfilUsuario.Psicologo,
                Status = StatusUsuario.Ativo
            });
        await contexto.SaveChangesAsync();

        var service = CriarBridge(contexto, protecao);
        await service.ReceberEventoAsync(
            token,
            new ReceberEventoBridgeRequest
            {
                EventoExternoId = "evt-sensivel",
                TipoEvento = "mensagem.recebida",
                Telefone = "5511999991111",
                Conteudo = "Estou em crise de panico e quero morrer"
            },
            null,
            null,
            CancellationToken.None);

        var conversa = Assert.Single(contexto.ConversasWhatsApp);
        Assert.True(conversa.AtendimentoHumanoNecessario);
        Assert.Equal(StatusConversaWhatsApp.AtendimentoHumano, conversa.Status);
        Assert.Contains(contexto.Notificacoes, n => n.Tipo == "whatsapp" && n.UsuarioId == usuarioId);
    }

    [Fact]
    public async Task EventoDuplicado_DeveSerIdempotente()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var token = "token-bridge-idempotente";
        var contaId = Guid.NewGuid();
        contexto.AddRange(
            new ContaPsicologo { Id = contaId, NomeConta = "Conta WA", Status = StatusContaPsicologo.Ativa },
            new IntegracaoWhatsApp
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                Nome = "Bridge",
                Provedor = "Bridge",
                UrlBaseBridge = "https://bridge.local",
                TokenCriptografado = protecao.Proteger(token),
                TokenHash = protecao.HashToken(token),
                NumeroWhatsApp = "5511888888888",
                Status = StatusIntegracao.Ativa,
                Ativo = true
            });
        await contexto.SaveChangesAsync();

        var service = CriarBridge(contexto, protecao);
        var request = new ReceberEventoBridgeRequest
        {
            EventoExternoId = "evt-dup",
            TipoEvento = "mensagem.recebida",
            Telefone = "5511999992222",
            Conteudo = "Olá",
            MensagemExternaId = "msg-dup"
        };

        await service.ReceberEventoAsync(token, request, null, null, CancellationToken.None);
        var segundo = await service.ReceberEventoAsync(token, request, null, null, CancellationToken.None);

        Assert.True(segundo.Duplicado);
        Assert.Single(contexto.ConversasWhatsApp);
        Assert.Equal(2, contexto.MensagensWhatsApp.Count());
    }

    [Theory]
    [InlineData("Quero falar com um atendente")]
    [InlineData("Posso falar com uma pessoa de verdade?")]
    [InlineData("Preciso da recepção")]
    public async Task PedidoParaFalarComPessoa_DevePassarParaEquipeEAvisarOPaciente(string texto)
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var (token, usuarioId) = await SemearContaAsync(contexto, protecao, comAgenda: true);

        await CriarBridge(contexto, protecao).ReceberEventoAsync(token, Mensagem("evt-humano", texto), null, null, CancellationToken.None);

        var conversa = Assert.Single(contexto.ConversasWhatsApp);
        Assert.Equal(StatusConversaWhatsApp.AtendimentoHumano, conversa.Status);
        Assert.True(conversa.AtendimentoHumanoNecessario);
        Assert.Equal("O paciente pediu para falar com uma pessoa.", conversa.MotivoAtendimentoHumano);
        Assert.Contains(contexto.MensagensWhatsApp, m => m.Direcao == DirecaoMensagem.Saida && protecao.Desproteger(m.Conteudo!).Contains("avisar a equipe"));
        Assert.Contains(contexto.Notificacoes, n => n.Tipo == "whatsapp" && n.UsuarioId == usuarioId);
    }

    [Fact]
    public async Task ConversaComEquipe_NaoDeveSerRespondidaPeloAssistente()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var (token, _) = await SemearContaAsync(contexto, protecao, comAgenda: true);
        var bridge = CriarBridge(contexto, protecao);

        await bridge.ReceberEventoAsync(token, Mensagem("evt-1", "atendente"), null, null, CancellationToken.None);
        var saidasAntes = contexto.MensagensWhatsApp.Count(m => m.Direcao == DirecaoMensagem.Saida);
        await bridge.ReceberEventoAsync(token, Mensagem("evt-2", "quais horários?"), null, null, CancellationToken.None);

        Assert.Equal(saidasAntes, contexto.MensagensWhatsApp.Count(m => m.Direcao == DirecaoMensagem.Saida));
        Assert.Equal(StatusConversaWhatsApp.AtendimentoHumano, Assert.Single(contexto.ConversasWhatsApp).Status);
    }

    [Fact]
    public async Task DuasMensagensIncompreensiveis_DevemPassarParaEquipe()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var (token, _) = await SemearContaAsync(contexto, protecao, comAgenda: true);
        var bridge = CriarBridge(contexto, protecao);

        await bridge.ReceberEventoAsync(token, Mensagem("evt-1", "bom dia, tudo bem?"), null, null, CancellationToken.None);
        Assert.Equal(StatusConversaWhatsApp.Aberta, Assert.Single(contexto.ConversasWhatsApp).Status);
        Assert.Contains(contexto.MensagensWhatsApp, m => m.Direcao == DirecaoMensagem.Saida && protecao.Desproteger(m.Conteudo!).Contains("ATENDENTE"));

        await bridge.ReceberEventoAsync(token, Mensagem("evt-2", "queria saber uma coisa"), null, null, CancellationToken.None);

        var conversa = Assert.Single(contexto.ConversasWhatsApp);
        Assert.Equal(StatusConversaWhatsApp.AtendimentoHumano, conversa.Status);
        Assert.Equal("O assistente não entendeu o pedido.", conversa.MotivoAtendimentoHumano);
    }

    [Fact]
    public async Task PedidoQueDependeDoCadastro_SemPacienteIdentificado_DevePassarParaEquipe()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var (token, _) = await SemearContaAsync(contexto, protecao, comAgenda: true);

        await CriarBridge(contexto, protecao).ReceberEventoAsync(token, Mensagem("evt-1", "quero cancelar minha consulta"), null, null, CancellationToken.None);

        var conversa = Assert.Single(contexto.ConversasWhatsApp);
        Assert.Equal(StatusConversaWhatsApp.AtendimentoHumano, conversa.Status);
        Assert.Contains("Paciente não identificado", conversa.MotivoAtendimentoHumano);
    }

    [Fact]
    public async Task ConteudoSensivel_DeveResponderAoPacienteComOsContatosDeApoio()
    {
        await using var contexto = CriarContexto();
        var protecao = new ProtecaoFake();
        var (token, _) = await SemearContaAsync(contexto, protecao, comAgenda: false);

        await CriarBridge(contexto, protecao).ReceberEventoAsync(token, Mensagem("evt-1", "não quero viver"), null, null, CancellationToken.None);

        var resposta = Assert.Single(contexto.MensagensWhatsApp, m => m.Direcao == DirecaoMensagem.Saida);
        var texto = protecao.Desproteger(resposta.Conteudo!);
        Assert.Contains("188", texto);
        Assert.Contains("192", texto);
    }

    private static ReceberEventoBridgeRequest Mensagem(string eventoId, string conteudo) => new()
    {
        EventoExternoId = eventoId,
        TipoEvento = "mensagem.recebida",
        Telefone = "5511977776666",
        Conteudo = conteudo,
        MensagemExternaId = $"msg-{eventoId}"
    };

    private static async Task<(string Token, Guid UsuarioId)> SemearContaAsync(AppDbContext contexto, ProtecaoFake protecao, bool comAgenda)
    {
        var token = $"token-{Guid.NewGuid():N}";
        var contaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        contexto.AddRange(
            new ContaPsicologo { Id = contaId, NomeConta = "Conta WA", Status = StatusContaPsicologo.Ativa },
            new IntegracaoWhatsApp
            {
                Id = Guid.NewGuid(),
                ContaPsicologoId = contaId,
                Nome = "Bridge",
                Provedor = "Bridge",
                UrlBaseBridge = "https://bridge.local",
                TokenCriptografado = protecao.Proteger(token),
                TokenHash = protecao.HashToken(token),
                NumeroWhatsApp = "5511888888888",
                Status = StatusIntegracao.Ativa,
                Ativo = true
            },
            new Usuario
            {
                Id = usuarioId,
                ContaPsicologoId = contaId,
                NomeCompleto = "Psi",
                Email = $"{usuarioId:N}@teste.local",
                SenhaHash = "x",
                Perfil = PerfilUsuario.Psicologo,
                Status = StatusUsuario.Ativo
            });
        if (comAgenda)
            contexto.Add(new ConfiguracaoAtendimento { Id = Guid.NewGuid(), ContaPsicologoId = contaId });
        await contexto.SaveChangesAsync();
        return (token, usuarioId);
    }

    private static BridgeWhatsAppService CriarBridge(AppDbContext contexto, ProtecaoFake? protecao = null)
    {
        protecao ??= new ProtecaoFake();
        return new BridgeWhatsAppService(
            contexto,
            protecao,
            new AuditoriaCaptura(),
            new NotificacoesService(contexto, new AutorizacaoContaService(contexto)),
            new DetectorConteudoSensiveisService());
    }

    private static AppDbContext CriarContexto() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

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
}

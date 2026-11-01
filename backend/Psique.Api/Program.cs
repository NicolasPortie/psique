using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Psique.Api.Configuracoes;
using Psique.Api.Data;
using Psique.Api.Middlewares;
using Psique.Api.Models;
using Psique.Api.Services.Autenticacao;
using Psique.Api.Services.Agenda;
using Psique.Api.Services.Comunicacao;
using Psique.Api.Services.Consultas;
using Psique.Api.Services.DadosProfissionais;
using Psique.Api.Services.Documentos;
using Psique.Api.Services.Lembretes;
using Psique.Api.Services.ListaEspera;
using Psique.Api.Services.ModelosMensagem;
using Psique.Api.Services.Notificacoes;
using Psique.Api.Services.Painel;
using Psique.Api.Services.PerfisAcesso;
using Psique.Api.Services.Pagamentos;
using Psique.Api.Services.Pacientes;
using Psique.Api.Services.Relatorios;
using Psique.Api.Services.Seguranca;
using Psique.Api.Services.Sessoes;
using Psique.Api.Services.Usuarios;
using Psique.Api.Services.WhatsApp;
using Psique.Api.Shared;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Respostas;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<JwtConfiguracao>()
    .Bind(builder.Configuration.GetSection(JwtConfiguracao.Secao))
    .Validate(
        configuracao => !string.IsNullOrWhiteSpace(configuracao.Emissor),
        "Emissor JWT obrigatório.")
    .Validate(
        configuracao => !string.IsNullOrWhiteSpace(configuracao.Audiencia),
        "Audiência JWT obrigatória.")
    .Validate(
        configuracao => configuracao.Chave.Length >= 32,
        "A chave JWT deve ter ao menos 32 caracteres.")
    .ValidateOnStart();

builder.Services
    .AddOptions<SegurancaConfiguracao>()
    .Bind(builder.Configuration.GetSection(SegurancaConfiguracao.Secao))
    .Validate(
        configuracao =>
            Convert.TryFromBase64String(
                configuracao.ChaveHashDados,
                new Span<byte>(new byte[64]),
                out var bytes) &&
            bytes >= 32,
        "A chave de hash de dados deve ser Base64 com ao menos 32 bytes.")
    .Validate(
        configuracao =>
            configuracao.MaximoTentativasLogin > 0 &&
            configuracao.DuracaoBloqueioLoginMinutos > 0 &&
            configuracao.MaximoSolicitacoesRecuperacao > 0 &&
            configuracao.MaximoCadastrosPorHora > 0 &&
            configuracao.DuracaoTokenRecuperacaoMinutos > 0,
        "Os limites e tempos de segurança devem ser maiores que zero.")
    .Validate(
        configuracao => !string.IsNullOrWhiteSpace(configuracao.VersaoTermosUso),
        "A versão dos termos de uso e obrigatória.")
    .ValidateOnStart();

builder.Services.Configure<RedisConfiguracao>(
    builder.Configuration.GetSection(RedisConfiguracao.Secao));
builder.Services.Configure<EmailConfiguracao>(
    builder.Configuration.GetSection(EmailConfiguracao.Secao));

var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("Psique");

// As chaves da Data Protection decifram todo dado sensível do banco. Em produção elas próprias ficam cifradas
// por um certificado (PFX) guardado fora do Redis e do banco; sem ele, quem lê o Redis lê as chaves.
var certificadoChaves = builder.Configuration["Seguranca:CertificadoChavesArquivo"];
if (!string.IsNullOrWhiteSpace(certificadoChaves))
{
    dataProtection.ProtectKeysWithCertificate(
        System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
            certificadoChaves,
            builder.Configuration["Seguranca:CertificadoChavesSenha"]));
}
else if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException("Seguranca:CertificadoChavesArquivo não configurado: as chaves de cifra ficariam sem proteção em repouso.");
}
var conexaoRedis = builder.Configuration.GetConnectionString("Redis");

if (!string.IsNullOrWhiteSpace(conexaoRedis))
{
    var redis = await ConnectionMultiplexer.ConnectAsync(conexaoRedis);

    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddStackExchangeRedisCache(opcoes =>
    {
        opcoes.ConnectionMultiplexerFactory = () =>
            Task.FromResult<IConnectionMultiplexer>(redis);
    });
    builder.Services.AddScoped<IEstadoAutenticacao, EstadoAutenticacaoRedis>();
    builder.Services.AddScoped<IEstadoConfirmacaoEmail, EstadoConfirmacaoEmailRedis>();
    dataProtection.PersistKeysToStackExchangeRedis(
        redis,
        "Psique:protecao-dados:chaves");
}
else if (builder.Environment.IsDevelopment())
{
    var diretorioChaves = Path.Combine(
        builder.Environment.ContentRootPath,
        ".psique-dev",
        "data-protection-keys");

    Directory.CreateDirectory(diretorioChaves);
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IEstadoAutenticacao, EstadoAutenticacaoMemoria>();
    builder.Services.AddSingleton<IEstadoConfirmacaoEmail, EstadoConfirmacaoEmailMemoria>();
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(diretorioChaves));
}
else
{
    throw new InvalidOperationException("ConnectionStrings:Redis não configurada.");
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.Configure<ApiBehaviorOptions>(opcoes =>
{
    opcoes.InvalidModelStateResponseFactory = contexto =>
    {
        var erros = contexto.ModelState.Values
            .SelectMany(valor => valor.Errors)
            .Select(erro => erro.ErrorMessage)
            .Where(mensagem => !string.IsNullOrWhiteSpace(mensagem))
            .ToArray();

        return new BadRequestObjectResult(
            RespostaApi<object>.ComErro("Dados inválidos.", erros));
    };
});

builder.Services.AddDbContext<AppDbContext>(opcoes =>
    opcoes.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddOpenApi();

var jwt = builder.Configuration
    .GetSection(JwtConfiguracao.Secao)
    .Get<JwtConfiguracao>()
    ?? throw new InvalidOperationException("Configuração JWT não encontrada.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opcoes =>
    {
        opcoes.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Emissor,
            ValidateAudience = true,
            ValidAudience = jwt.Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.Chave)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        opcoes.Events = new JwtBearerEvents
        {
            OnTokenValidated = async contexto =>
            {
                var validacao = contexto.HttpContext.RequestServices
                    .GetRequiredService<ValidacaoSessaoJwtService>();
                if (contexto.Principal is null ||
                    !await validacao.ValidarAsync(contexto.Principal, contexto.HttpContext.RequestAborted))
                    contexto.Fail("Sessão inválida ou revogada.");
            }
        };
    });
builder.Services.AddAuthorization(opcoes =>
{
    opcoes.AddPolicy(Politicas.GestaoAdministrativa, politica => politica.RequireRole(
        PerfilUsuario.Psicologo.ToString(),
        PerfilUsuario.Recepcionista.ToString(),
        PerfilUsuario.Administrador.ToString(),
        PerfilUsuario.Personalizado.ToString()));
    opcoes.AddPolicy(Politicas.AcessoClinico, politica => politica.RequireRole(
        PerfilUsuario.Psicologo.ToString()));
    opcoes.AddPolicy(Politicas.GestaoConta, politica => politica.RequireRole(
        PerfilUsuario.Psicologo.ToString(),
        PerfilUsuario.Administrador.ToString()));

    // Uma política por permissão do catálogo. As claims "permissao" são montadas a cada requisição
    // a partir do banco (ValidacaoSessaoJwtService), nunca lidas do token.
    foreach (var permissao in Enum.GetValues<PermissaoAcesso>())
        opcoes.AddPolicy(Politicas.De(permissao), politica => politica.RequireClaim(ValidacaoSessaoJwtService.ClaimPermissao, permissao.ToString()));
});

// Atrás de um proxy reverso (nginx, balanceador), o IP do cliente vem em X-Forwarded-For. Só se confia
// nele quando a implantação declara o proxy, e apenas vindo de redes privadas: sem isso qualquer cliente
// poderia forjar o cabeçalho e fugir dos limites por IP.
if (builder.Configuration.GetValue<bool>("Rede:ConfiarEmProxyReverso"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(opcoes =>
    {
        opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        opcoes.ForwardLimit = 1;
        foreach (var rede in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" })
            opcoes.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(rede));
    });
}

// Limite de volume por IP nos endpoints sem sessão (cadastro, login, recuperação e webhook do Bridge).
// É complementar às travas por conta e por e-mail do serviço de autenticação, que tratam força bruta.
builder.Services.AddRateLimiter(opcoes =>
{
    opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opcoes.OnRejected = async (contexto, ct) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "60";
        await contexto.HttpContext.Response.WriteAsJsonAsync(
            RespostaApi<object>.ComErro("Muitas requisições. Aguarde um instante e tente novamente."),
            ct);
    };
    // Uma cota por IP e por endpoint: inundar a recuperação de senha não pode travar o login do consultório.
    opcoes.AddPolicy("sem-sessao", contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{contexto.Connection.RemoteIpAddress}|{contexto.Request.Path.Value?.ToLowerInvariant()}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddCors(opcoes =>
{
    var origensFrontend = builder.Configuration
        .GetSection("Frontend:OrigensPermitidas")
        .Get<string[]>()
        ?? [builder.Configuration["Frontend:UrlBase"] ?? "http://localhost:4200"];

    opcoes.AddPolicy("FrontendPsique", politica =>
        politica
            .WithOrigins(origensFrontend)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<IAutenticacaoService, AutenticacaoService>();
builder.Services.AddScoped<IConfirmacaoEmailCadastroService, ConfirmacaoEmailCadastroService>();
builder.Services.AddScoped<IUsuariosService, UsuariosService>();
builder.Services.AddScoped<IPerfisAcessoService, PerfisAcessoService>();
builder.Services.AddScoped<IDadosProfissionaisService, DadosProfissionaisService>();
builder.Services.AddScoped<IPainelService, PainelService>();
builder.Services.AddScoped<IPacientesService, PacientesService>();
builder.Services.AddScoped<IProtecaoDadosService, ProtecaoDadosService>();
builder.Services.AddScoped<IAuditoriaService, AuditoriaService>();
builder.Services.AddScoped<IAutorizacaoContaService, AutorizacaoContaService>();
builder.Services.AddScoped<ValidacaoSessaoJwtService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailTransacionalService, EmailTransacionalService>();
builder.Services.AddScoped<IAgendaService, AgendaService>();
builder.Services.AddScoped<IConsultasService, ConsultasService>();
builder.Services.AddScoped<IDocumentosService, DocumentosService>();
builder.Services.AddScoped<ISessoesService, SessoesService>();
builder.Services.AddScoped<IPagamentosService, PagamentosService>();
builder.Services.AddScoped<IListaEsperaService, ListaEsperaService>();
builder.Services.AddScoped<IModeloMensagemTemplateService, ModeloMensagemTemplateService>();
builder.Services.AddScoped<IModelosMensagemService, ModelosMensagemService>();
builder.Services.AddScoped<ILembretesService, LembretesService>();
builder.Services.AddHostedService<ProcessadorLembretesBackgroundService>();
builder.Services.AddSingleton<RelatorioHtmlRenderer>();
builder.Services.AddScoped<IRelatoriosAdministrativosService, RelatoriosAdministrativosService>();
builder.Services.AddScoped<INotificacoesService, NotificacoesService>();
builder.Services.AddHttpClient("BridgeWhatsApp");
builder.Services.AddScoped<EvolutionApiClient>();
builder.Services.AddScoped<WebhookEvolutionService>();
builder.Services.AddScoped<EnvioWhatsAppService>();
builder.Services.AddHostedService<ProcessadorWhatsAppBackgroundService>();
builder.Services.AddSingleton<DetectorConteudoSensiveisService>();
builder.Services.AddScoped<IIntegracaoWhatsAppService, IntegracaoWhatsAppService>();
builder.Services.AddScoped<IConversasWhatsAppService, ConversasWhatsAppService>();
builder.Services.AddScoped<IBridgeWhatsAppService, BridgeWhatsAppService>();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Rede:ConfiarEmProxyReverso"))
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CabecalhosSegurancaMiddleware>();
app.UseMiddleware<TratamentoExcecoesMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors("FrontendPsique");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Cifra de coluna: configurada antes de qualquer acesso ao banco; depois, protege o que ainda estiver em texto aberto.
Psique.Api.Data.Cifra.CifraDeColuna.Configurar(
    app.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
    Convert.FromBase64String(app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SegurancaConfiguracao>>().Value.ChaveHashDados));
using (var escopo = app.Services.CreateScope())
{
    var registro = escopo.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CifraDeColuna");
    try
    {
        await Psique.Api.Data.Cifra.MigracaoCifraColunas.ExecutarAsync(
            escopo.ServiceProvider.GetRequiredService<AppDbContext>(),
            registro,
            escopo.ServiceProvider.GetRequiredService<IProtecaoDadosService>());
    }
    catch (Exception erro)
    {
        // Banco ainda sem as migrações aplicadas: a API sobe e a migração roda na próxima subida.
        registro.LogError(erro, "Não foi possível cifrar os dados antigos nesta subida.");
    }
}

app.Run();

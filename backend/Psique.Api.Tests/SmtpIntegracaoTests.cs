using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Psique.Api.Configuracoes;
using Psique.Api.Services.Comunicacao;

namespace Psique.Api.Tests;

public sealed class SmtpIntegracaoTests
{
    [SmtpFact]
    public async Task TransporteReal_EnviaRecuperacaoEAdministrativoComStartTls()
    {
        var esperado = Environment.GetEnvironmentVariable("PSIQUE_TEST_SMTP_CERT")!;
        var config = Options.Create(new EmailConfiguracao
        {
            Ativo = true, ServidorSmtp = "127.0.0.1", Porta = 11025, UsarSsl = false,
            EmailRemetente = "vinculo@teste.invalid", NomeRemetente = "Vínculo",
            UrlFrontend = "https://localhost"
        });
        var servico = new EmailTransacionalService(config, NullLogger<EmailTransacionalService>.Instance,
            () => new SmtpClient
            {
                // Confiança restrita ao certificado efêmero deste teste; produção usa a validação padrão.
                ServerCertificateValidationCallback = (_, certificado, _, _) => certificado?.GetCertHashString() == esperado
            });
        var destinatario = $"validacao-{Guid.NewGuid():N}@teste.invalid";
        Assert.True(await servico.EnviarRecuperacaoSenhaAsync(destinatario, "Teste", "token-descartavel", default));
        Assert.True(await servico.EnviarMensagemAdministrativaAsync(destinatario, "Teste", "Lembrete de teste", "Consulta de homologação", default));
        using var http = new HttpClient();
        var mensagens = await http.GetStringAsync($"http://127.0.0.1:18025/api/v1/search?query=to:{Uri.EscapeDataString(destinatario)}");
        using var json = System.Text.Json.JsonDocument.Parse(mensagens);
        Assert.Equal(2, json.RootElement.GetProperty("messages").GetArrayLength());
    }
}

public sealed class SmtpFactAttribute : FactAttribute
{
    public SmtpFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PSIQUE_TEST_SMTP_CERT")))
            Skip = "Defina PSIQUE_TEST_SMTP_CERT para o Mailpit local com STARTTLS.";
    }
}

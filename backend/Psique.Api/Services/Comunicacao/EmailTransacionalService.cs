using Psique.Api.Configuracoes;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Psique.Api.Services.Comunicacao;

public sealed class EmailTransacionalService(
    IOptions<EmailConfiguracao> configuracao,
    ILogger<EmailTransacionalService> logger,
    Func<SmtpClient>? criarCliente = null)
    : IEmailTransacionalService
{
    private readonly EmailConfiguracao _configuracao = configuracao.Value;

    public async Task<bool> EnviarRecuperacaoSenhaAsync(
        string destinatario,
        string nome,
        string token,
        CancellationToken cancellationToken)
    {
        if (!_configuracao.Ativo)
        {
            logger.LogWarning("Envio de e-mail transacional está desabilitado.");
            return false;
        }

        var link = $"{_configuracao.UrlFrontend.TrimEnd('/')}/redefinir-senha?token={Uri.EscapeDataString(token)}";
        var saudacao = string.IsNullOrWhiteSpace(nome) ? "Olá." : $"Olá, {nome}.";
        var corpo = $"""
            {saudacao}

            Recebemos uma solicitação para redefinir sua senha no {_configuracao.NomeRemetente}.
            Use o link abaixo. Ele expira em poucos minutos e pode ser utilizado uma única vez.

            {link}

            Se você não solicitou esta alteração, ignore esta mensagem.
            """;

        return await EnviarAsync(
            destinatario,
            $"Recuperação de acesso ao {_configuracao.NomeRemetente}",
            corpo,
            cancellationToken);
    }

    public async Task<bool> EnviarMensagemAdministrativaAsync(
        string destinatario,
        string nomeDestinatario,
        string assunto,
        string corpo,
        CancellationToken cancellationToken)
    {
        if (!_configuracao.Ativo)
        {
            logger.LogWarning("Envio de e-mail administrativo está desabilitado.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinatario) ||
            string.IsNullOrWhiteSpace(assunto) ||
            string.IsNullOrWhiteSpace(corpo))
        {
            return false;
        }

        var corpoFormatado = string.IsNullOrWhiteSpace(nomeDestinatario)
            ? corpo
            : $"Olá, {nomeDestinatario}.\n\n{corpo}";

        return await EnviarAsync(destinatario, assunto.Trim(), corpoFormatado, cancellationToken);
    }

    private async Task<bool> EnviarAsync(
        string destinatario,
        string assunto,
        string corpo,
        CancellationToken cancellationToken)
    {
        var mensagem = new MimeMessage();
        mensagem.From.Add(new MailboxAddress(
            _configuracao.NomeRemetente,
            _configuracao.EmailRemetente));
        mensagem.To.Add(MailboxAddress.Parse(destinatario));
        mensagem.Subject = assunto;
        mensagem.Body = new TextPart("plain") { Text = corpo };

        try
        {
            using var cliente = criarCliente?.Invoke() ?? new SmtpClient();
            cliente.Timeout = 10000;
            var opcaoSsl = _configuracao.UsarSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await cliente.ConnectAsync(
                _configuracao.ServidorSmtp,
                _configuracao.Porta,
                opcaoSsl,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_configuracao.Usuario))
            {
                await cliente.AuthenticateAsync(
                    _configuracao.Usuario,
                    _configuracao.Senha,
                    cancellationToken);
            }

            await cliente.SendAsync(mensagem, cancellationToken);
            await cliente.DisconnectAsync(true, cancellationToken);
            return true;
        }
        catch (Exception excecao)
        {
            logger.LogError("Falha no envio de e-mail transacional ({Tipo}).", excecao.GetType().Name);
            return false;
        }
    }
}

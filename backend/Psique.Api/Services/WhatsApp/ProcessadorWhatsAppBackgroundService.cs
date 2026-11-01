namespace Psique.Api.Services.WhatsApp;

public sealed class ProcessadorWhatsAppBackgroundService(IServiceScopeFactory fabrica, ILogger<ProcessadorWhatsAppBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ciclo = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await ciclo.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var escopo = fabrica.CreateScope();
                    await escopo.ServiceProvider.GetRequiredService<EnvioWhatsAppService>().ProcessarPendentesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    // Exceções HTTP/EF podem conter endereços e dados de pacientes.
                    logger.LogError("Falha no processamento da fila de WhatsApp. As mensagens iniciadas exigem conferência no provedor.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

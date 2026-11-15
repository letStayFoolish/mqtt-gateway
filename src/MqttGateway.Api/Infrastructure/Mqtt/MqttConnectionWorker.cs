namespace MqttGateway.Api.Infrastructure.Mqtt;

public class MqttConnectionWorker(MqttConnection connection, ILogger<MqttConnectionWorker> logger) : BackgroundService
{
    private const int HealthyIntervalSeconds = 5;
    private const int BackoffBaseSeconds = 1;
    private const int BackoffCapSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(HealthyIntervalSeconds);

                try
                {
                    if (!await connection.IsAliveAsync(stoppingToken))
                    {
                        await connection.ConnectAsync(stoppingToken);
                        logger.LogInformation("Mqtt Connection Worker Connected.");
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    int failedAttempts = connection.ReportFailure(exception);
                    delay = NextBackoff(failedAttempts);
                    logger.LogWarning(
                        "MQTT connect failed (attempt {Attempt}): {ExceptionType}: {Reason}. Retrying in {DelaySeconds:F1}s.",
                        failedAttempts,
                        exception.GetType().Name,
                        exception.GetBaseException().Message,
                        delay.TotalSeconds);

                    logger.LogDebug(exception, "MQTT connect failure detail: (attemt {Attempt})", failedAttempts);
                }

                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("MQTT worker stopping.");
        }
        finally
        {
            await DisconnectQuietlyAsync();
        }
    }

    private async Task DisconnectQuietlyAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.DisconnectAsync(timeout.Token);
            logger.LogInformation("MQTT client disconnected cleanly.");
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Clean MQTT disconnect failed during shutdown.");
        }
    }
   
    
    private static TimeSpan NextBackoff(int attempt)
    {
        var ceiling = Math.Min(BackoffCapSeconds, BackoffBaseSeconds * Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(Random.Shared.NextDouble() * ceiling);
    }
}
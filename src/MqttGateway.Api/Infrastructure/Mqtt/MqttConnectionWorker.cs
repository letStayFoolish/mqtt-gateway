using Microsoft.Extensions.Options;
using MQTTnet;

namespace MqttGateway.Api.Infrastructure.Mqtt;

public class MqttConnectionWorker(IOptions<MqttOptions> options, ILogger<MqttConnectionWorker> logger) : BackgroundService
{
    private readonly string host = options.Value.Host;
    private readonly string clientIdPrefix = $"{options.Value.ClientIdPrefix}{Guid.NewGuid().ToString("N")[..8]}";
    private readonly int port = options.Value.Port;
    
    //Constants
    private const int HealthyIntervalSeconds = 5;
    private const int BackoffBaseSeconds = 1;
    private const int BackoffCapSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        var clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId(clientIdPrefix)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        var failedAttempts = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(HealthyIntervalSeconds);
                
                try
                {
                    if (!await client.TryPingAsync(cancellationToken: stoppingToken))
                    {
                        await client.ConnectAsync(clientOptions, stoppingToken);
                        failedAttempts = 0;
                        logger.LogInformation("Mqtt Connection Worker Connected.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedAttempts++;
                    delay = NextBackoff(failedAttempts);
                    logger.LogWarning(
                        "MQTT connect failed (attempt {Attempt}): {ExceptionType}: {Reason}. Retrying in {DelaySeconds:F1}s.",
                        failedAttempts,
                        ex.GetType().Name,
                        ex.GetBaseException().Message,
                        delay.TotalSeconds);
                    
                    logger.LogDebug(ex, "MQTT connect failure detail: (attemt {Attemt})", failedAttempts);
                }

                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogError("Connection lost. Error: {ex}", ex);
        }
    }
    
    private static TimeSpan NextBackoff(int attempt)
    {
        var ceiling = Math.Min(BackoffCapSeconds, BackoffBaseSeconds * Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(Random.Shared.NextDouble() * ceiling);
    }
}
using Microsoft.Extensions.Options;
using MQTTnet;

namespace MqttGateway.Api.Infrastructure.Mqtt;

public class MqttConnectionWorker(IOptions<MqttOptions> options, ILogger<MqttConnectionWorker> logger) : BackgroundService
{
    private readonly string host = options.Value.Host;
    private readonly string clientIdPrefix = $"{options.Value.ClientIdPrefix}{Guid.NewGuid().ToString("N")[..8]}";
    private readonly int port = options.Value.Port;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        var clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId(clientIdPrefix)
            .Build();

        const int retryDelaySeconds = 5;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await client.TryPingAsync())
                    {
                        await client.ConnectAsync(clientOptions, stoppingToken);
                        logger.LogInformation("Mqtt Connection Worker Connected.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Imposible to connect. Trying again in {retryDelaySeconds} seconds...", retryDelaySeconds);
                }

                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogError("Connection lost. Error: {ex}", ex);
        }
    }
}
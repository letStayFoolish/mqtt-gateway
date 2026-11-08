using Microsoft.Extensions.Options;
using MQTTnet;

namespace MqttGateway.Api.Infrastructure.Mqtt;

public sealed class MqttConnection : IDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _mqttOptions;

    private DateTimeOffset? _connectedSince;
    private string? _lastError;
    private int _failedAttempts;
    public MqttConnection(IOptions<MqttOptions> options)
    {

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _mqttOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(options.Value.Host, options.Value.Port)
            .WithClientId($"{options.Value.ClientIdPrefix}{Guid.NewGuid().ToString("N")[..8]}")
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();
    }

    public MqttConnectionStatus Status => new(
        _client.IsConnected,
        _client.IsConnected ? _connectedSince : null,
        _lastError,
        _failedAttempts);

    public Task<bool> IsAliveAsync(CancellationToken ct)
    {
        return _client.TryPingAsync(ct);
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _client.ConnectAsync(_mqttOptions, ct);

        //after connect
        _connectedSince = DateTimeOffset.UtcNow;
        _lastError = null;
        _failedAttempts = 0;
    }

    public int ReportFailure(Exception exception)
    {

        _lastError =  $"{exception.GetType().Name}: {exception.GetBaseException().Message}";

        return ++_failedAttempts;
    }

    public Task DisconnectAsync(CancellationToken ct)
    {
        return _client.DisconnectAsync(cancellationToken: ct);
    }

    public void Dispose() => _client.Dispose();
}

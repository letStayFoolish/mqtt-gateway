namespace MqttGateway.Api.Infrastructure.Mqtt;

public record MqttConnectionStatus(
    bool IsConnected,
    DateTimeOffset? ConnectedSince,
    string? LastError,
    int FailedAttempts);
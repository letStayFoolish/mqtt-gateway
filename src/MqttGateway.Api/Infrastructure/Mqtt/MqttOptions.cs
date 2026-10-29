using System.ComponentModel.DataAnnotations;

namespace MqttGateway.Api.Infrastructure.Mqtt;

public sealed class MqttOptions
{
    public const string SectionName = "MqttOptions";

    [Required]
    public required string Host { get; init; }

    [Range(1, 65535)]
    public required int Port { get; init; }

    [Required]
    [StringLength(50)]
    public required string ClientIdPrefix { get; init; }

    [StringLength(100)]
    public string? Username { get; init; }

    public string? Password { get; init; }
}

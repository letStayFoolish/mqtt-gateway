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
    [MaxLength(50)]
    public required string ClientId { get; init; }

    [Required]
    [MaxLength(100)]
    public required string Username { get; init; }

    [Required]
    public required string Password { get; init; }
}

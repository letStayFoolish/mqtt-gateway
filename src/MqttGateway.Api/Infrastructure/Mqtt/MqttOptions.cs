using System.ComponentModel.DataAnnotations;

namespace MqttGateway.Api.Infrastructure.Mqtt;

public sealed class MqttOptions
{
    public const string SectionName = "MqttOptions";

    [Required]
    public string Host { get; set; }

    [Required]
    [Range(1, 65535)]
    public int Port { get; set; }

    [Required]
    [MaxLength(50)]
    public string ClientId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; }

    [Required]
    [MaxLength(10)]
    public string Password { get; set; }
}
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MqttGateway.Api.Infrastructure.Mqtt;

namespace MqttGateway.UnitTests.Infrastructure.Mqtt;

public class MqttOptionsValidationTests
{
    //  "MqttOptions": {
    //   "Host": "localhost",
    //   "Port": 1883,
    //   "ClientIdPrefix": "Mqtt-ClientId-"
    // },
    [Fact]
    public async Task ValidMqttOptionsConfig_Returns_Ok()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{MqttOptions.SectionName}:Host"] = "localhost",
            [$"{MqttOptions.SectionName}:Port"] = "1883",
            [$"{MqttOptions.SectionName}:ClientIdPrefix"] = "test-"
        };
        
        using var host = BuildHost(settings);

        var exception = await Record.ExceptionAsync(() => host.StartAsync());
        
        Assert.Null(exception);
    }

    [Fact]
    public async Task InvalidSettingsData_ThrowsOptionValidationException()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{MqttOptions.SectionName}:Host"] = "localhost",
            [$"{MqttOptions.SectionName}:Port"] = "0",
            [$"{MqttOptions.SectionName}:ClientIdPrefix"] = "test-"
        };
        
        using var host = BuildHost(settings);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task StartAsync_Throws_WhenPortIsMissing()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{MqttOptions.SectionName}:Host"] = "localhost",
            [$"{MqttOptions.SectionName}:ClientIdPrefix"] = "test-"
        };
        
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services
            .AddOptionsWithValidateOnStart<MqttOptions>()
            .BindConfiguration(MqttOptions.SectionName)
            .ValidateDataAnnotations();
        
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }
    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services
            .AddOptionsWithValidateOnStart<MqttOptions>()
            .BindConfiguration(MqttOptions.SectionName)
            .ValidateDataAnnotations();
        return builder.Build();
    }
}
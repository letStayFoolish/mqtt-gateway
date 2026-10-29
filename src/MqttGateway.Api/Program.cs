using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MqttGateway.Api.Infrastructure.Mqtt;
using Serilog;
using Serilog.Events;

const string readyTag = "ready";
const string healthPathPrefix = "/health";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    builder.Services
        .AddOptionsWithValidateOnStart<MqttOptions>()
        .BindConfiguration(MqttOptions.SectionName)
        .ValidateDataAnnotations();

    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks(); // later I plan to add DbContext and Redis tag cheks.

    var app = builder.Build();

    app.UseSerilogRequestLogging(options => options.GetLevel = RequestLogLevel);

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.MapHealthChecks($"{healthPathPrefix}/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapHealthChecks($"{healthPathPrefix}/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains(readyTag)
    });
    
    app.Run();

    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Application terminated unexpectedly.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

LogEventLevel RequestLogLevel(HttpContext context, double elapsedMilliseconds, Exception? exception)
{
    if (exception is not null || context.Response.StatusCode >= 500)
    {
        return LogEventLevel.Error;
    }

    return context.Request.Path.StartsWithSegments(healthPathPrefix)
        ? LogEventLevel.Verbose
        : LogEventLevel.Information;
}

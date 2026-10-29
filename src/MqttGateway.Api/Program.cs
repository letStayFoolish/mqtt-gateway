using MqttGateway.Api.Infrastructure.Mqtt;
using Serilog;
using Serilog.Events;

const string healthCheckPath = "/healthz";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
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
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    app.UseSerilogRequestLogging(options => options.GetLevel = RequestLogLevel);

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.MapHealthChecks(healthCheckPath);

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

    return context.Request.Path.StartsWithSegments(healthCheckPath)
        ? LogEventLevel.Verbose
        : LogEventLevel.Information;
}

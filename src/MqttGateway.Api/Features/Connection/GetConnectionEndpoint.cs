using MqttGateway.Api.Infrastructure.Mqtt;

namespace MqttGateway.Api.Features.Connection;

public static class GetConnectionEndpoint
{
    public static void MapGetConnection(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/connection", (MqttConnection connection) => connection.Status);
}

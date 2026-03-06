namespace BuildingSentinel.Common.Endpoints;

using BuildingSentinel.Common.Commands;
using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Emit.Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Registers the minimal-API endpoints for the building-sentinel sample.
/// </summary>
public static class BuildingEventEndpoints
{
    /// <summary>Maps <c>POST /api/events</c> and <c>GET /api/devices/status</c> onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapBuildingEventEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/api/events").WithTags("Events");

        events.MapPost("/", async (
            BuildingEventRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var evt = new BuildingEvent
            {
                DeviceId = request.DeviceId,
                EventType = request.EventType,
                Location = request.Location,
                OccurredAt = DateTimeOffset.UtcNow,
                Metadata = request.Metadata
            };

            await mediator.SendAsync(new SubmitBuildingEventCommand(evt), ct);

            return Results.Ok(new { accepted = true, deviceId = evt.DeviceId, eventType = evt.EventType });
        })
        .WithSummary("Submit a device event from a building sensor or access reader")
        .WithDescription("Persists the event and publishes it to Kafka via the transactional outbox.");

        var devices = app.MapGroup("/api/devices").WithTags("Devices");

        devices.MapGet("/status", async (
            IDeviceHeartbeatRepository repo,
            TimeSpan? silentFor,
            CancellationToken ct) =>
        {
            var threshold = silentFor ?? TimeSpan.FromMinutes(5);
            var silent = await repo.GetSilentDevicesAsync(threshold, ct);
            return Results.Ok(silent);
        })
        .WithSummary("List devices that have gone silent")
        .WithDescription("Returns devices that have not sent any event within the specified duration (default: 5 minutes).");

        return app;
    }
}

/// <summary>HTTP request body for <c>POST /api/events</c>.</summary>
/// <param name="DeviceId">Identifier of the device reporting the event.</param>
/// <param name="EventType">Event classification string.</param>
/// <param name="Location">Human-readable physical location of the device.</param>
/// <param name="Metadata">Optional supplementary key-value metadata.</param>
public sealed record BuildingEventRequest(
    string DeviceId,
    string EventType,
    string Location,
    IReadOnlyDictionary<string, string>? Metadata = null);

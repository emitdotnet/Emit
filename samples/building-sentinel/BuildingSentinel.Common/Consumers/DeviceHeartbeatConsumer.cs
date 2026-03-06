namespace BuildingSentinel.Common.Consumers;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Emit.Abstractions;

/// <summary>
/// Handles every event in the <c>building.watchdog</c> consumer group by upserting a device-heartbeat record,
/// enabling liveness tracking for all sensors in the building.
/// </summary>
public sealed class DeviceHeartbeatConsumer(
    IDeviceHeartbeatRepository heartbeatRepository) : IConsumer<BuildingEvent>
{
    public async Task ConsumeAsync(
        InboundContext<BuildingEvent> context,
        CancellationToken cancellationToken)
    {
        var evt = context.Message;

        await heartbeatRepository
            .UpsertHeartbeatAsync(evt.DeviceId, evt.OccurredAt, cancellationToken)
            .ConfigureAwait(false);
    }
}

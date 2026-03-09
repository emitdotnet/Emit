namespace BuildingSentinel.Common.Consumers;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Emit.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Handles <c>access.denied</c> events routed by the <c>building.classifier</c> consumer group.
/// Increments the per-badge denial counter and emits a warning log when the alarm threshold is crossed.
/// </summary>
public sealed class AccessDeniedConsumer(
    IAlertRepository alertRepository,
    ILogger<AccessDeniedConsumer> logger) : IConsumer<BuildingEvent>
{
    private const int AlarmThreshold = 3;

    public async Task ConsumeAsync(
        ConsumeContext<BuildingEvent> context,
        CancellationToken cancellationToken)
    {
        var evt = context.Message;

        var alert = await alertRepository
            .IncrementDenialAsync(evt.DeviceId, AlarmThreshold, cancellationToken)
            .ConfigureAwait(false);

        if (alert.AlarmRaised)
        {
            logger.LogWarning(
                "ALARM: device {DeviceId} at {Location} has {DenialCount} consecutive access denials",
                evt.DeviceId, evt.Location, alert.DenialCount);
        }
        else
        {
            logger.LogInformation(
                "Access denied: device {DeviceId} at {Location} (denial #{DenialCount})",
                evt.DeviceId, evt.Location, alert.DenialCount);
        }
    }
}

namespace BuildingSentinel.Common.Daemons;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Watches device heartbeats and raises an alert when a device goes silent. It runs on exactly one instance of the
/// application at a time: on every instance, each silent device would be alerted once per instance.
/// </summary>
public sealed class DeviceOfflineDaemon(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DeviceOfflineDaemon> logger) : IDaemonAgent
{
    public string DaemonId => "device-offline-detector";

    public async Task RunAsync(DaemonRunContext context)
    {
        var silentFor = configuration.GetValue<TimeSpan?>("DeviceMonitor:SilentFor") ?? TimeSpan.FromSeconds(90);
        var checkEvery = configuration.GetValue<TimeSpan?>("DeviceMonitor:CheckEvery") ?? TimeSpan.FromSeconds(5);

        logger.LogInformation(
            "[Daemon] Device offline detector now runs on this instance (generation {Generation})", context.Generation);

        using var timer = new PeriodicTimer(checkEvery);
        while (await timer.WaitForNextTickAsync(context.AssignmentToken).ConfigureAwait(false))
        {
            try
            {
                await CheckAsync(context, silentFor).ConfigureAwait(false);
            }
            catch (Exception ex) when (!context.AssignmentToken.IsCancellationRequested)
            {
                // A database or broker hiccup is retried on the next check rather than failing the run
                logger.LogWarning(ex, "[Daemon] Device check failed; retrying on the next one");
            }
        }
    }

    private async Task CheckAsync(DaemonRunContext context, TimeSpan silentFor)
    {
        IReadOnlyList<DeviceHeartbeat> silentDevices;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            silentDevices = await scope.ServiceProvider.GetRequiredService<IDeviceHeartbeatRepository>()
                .GetSilentDevicesAsync(silentFor, context.AssignmentToken).ConfigureAwait(false);
        }

        foreach (var device in silentDevices)
        {
            // Another instance may run the daemon as soon as this one no longer holds it
            if (!context.HoldsAssignment)
            {
                return;
            }

            var alert = new DeviceOfflineAlert
            {
                DeviceId = device.DeviceId,
                LastSeenAt = device.LastSeenAt,
                DetectedAt = DateTimeOffset.UtcNow,
            };

            // One scope and one transaction per alert: the alert and its outbox entry commit together, or not at all
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            await using var transaction = await services.GetRequiredService<IUnitOfWork>()
                .BeginAsync(context.AssignmentToken).ConfigureAwait(false);

            var recorded = await services.GetRequiredService<IDeviceAlertRepository>()
                .TryRecordOfflineAsync(alert, context.FencingKey, context.Generation, context.AssignmentToken)
                .ConfigureAwait(false);
            if (!recorded)
            {
                continue;
            }

            await services.GetRequiredService<IEventProducer<string, DeviceOfflineAlert>>()
                .ProduceAsync(alert.DeviceId, alert, context.AssignmentToken).ConfigureAwait(false);
            await transaction.CommitAsync(context.AssignmentToken).ConfigureAwait(false);

            logger.LogWarning(
                "[Daemon] OFFLINE: device {DeviceId} silent since {LastSeenAt:HH:mm:ss}", alert.DeviceId, alert.LastSeenAt);
        }
    }
}

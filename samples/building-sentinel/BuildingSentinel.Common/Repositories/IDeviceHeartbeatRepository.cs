namespace BuildingSentinel.Common.Repositories;

using BuildingSentinel.Common.Domain;

/// <summary>
/// Tracks liveness information for building devices.
/// </summary>
public interface IDeviceHeartbeatRepository
{
    /// <summary>Records a heartbeat for <paramref name="deviceId"/> at <paramref name="seenAt"/>, creating the record on first contact.</summary>
    Task UpsertHeartbeatAsync(
        string deviceId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default);

    /// <summary>Returns devices that have not reported any event within <paramref name="silentFor"/>.</summary>
    Task<IReadOnlyList<DeviceHeartbeat>> GetSilentDevicesAsync(
        TimeSpan silentFor,
        CancellationToken cancellationToken = default);
}

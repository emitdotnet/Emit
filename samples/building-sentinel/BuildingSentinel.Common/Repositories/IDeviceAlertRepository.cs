namespace BuildingSentinel.Common.Repositories;

using BuildingSentinel.Common.Domain;

/// <summary>
/// Records device offline alerts, within the caller's active transaction.
/// </summary>
public interface IDeviceAlertRepository
{
    /// <summary>
    /// Records <paramref name="alert"/> unless that silence of the device was already alerted, or a later run of the
    /// daemon raising it took over. The daemon's <paramref name="fencingKey"/> keeps the highest
    /// <paramref name="generation"/> that wrote, and refuses any lower one.
    /// </summary>
    /// <returns><c>true</c> when the alert was recorded and should be published.</returns>
    Task<bool> TryRecordOfflineAsync(
        DeviceOfflineAlert alert,
        Guid fencingKey,
        long generation,
        CancellationToken cancellationToken = default);
}

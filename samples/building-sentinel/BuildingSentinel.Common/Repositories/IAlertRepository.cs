namespace BuildingSentinel.Common.Repositories;

using BuildingSentinel.Common.Domain;

/// <summary>
/// Maintains the running access-denial alert state per badge.
/// </summary>
public interface IAlertRepository
{
    /// <summary>
    /// Increments the denial counter for <paramref name="badgeId"/> and returns the updated alert state.
    /// Sets <see cref="AccessDenialAlert.AlarmRaised"/> when the count first reaches <paramref name="threshold"/>.
    /// </summary>
    Task<AccessDenialAlert> IncrementDenialAsync(
        string badgeId,
        int threshold,
        CancellationToken cancellationToken = default);
}

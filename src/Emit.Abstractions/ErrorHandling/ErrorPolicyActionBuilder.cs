namespace Emit.Abstractions.ErrorHandling;

/// <summary>
/// Extends <see cref="ErrorActionBuilder"/> with retry actions for error policy configuration.
/// After calling <see cref="Retry"/>, only <see cref="ErrorActionBuilder.DeadLetter"/> or
/// <see cref="ErrorActionBuilder.Discard"/> are available as the exhaustion action.
/// </summary>
public sealed class ErrorPolicyActionBuilder : ErrorActionBuilder
{
    private bool retryConfigured;

    /// <summary>
    /// Retry processing the message up to the specified number of attempts with the given backoff strategy.
    /// A terminal action (<see cref="ErrorActionBuilder.DeadLetter"/> or <see cref="ErrorActionBuilder.Discard"/>)
    /// must be chained after this call to define the exhaustion behavior.
    /// </summary>
    /// <param name="maxAttempts">The maximum number of retry attempts. Must be greater than zero.</param>
    /// <param name="backoff">The backoff strategy to apply between retries.</param>
    /// <returns>The base builder, restricting further calls to terminal actions only.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is less than or equal to zero.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="backoff"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An action or retry has already been configured.</exception>
    public ErrorActionBuilder Retry(int maxAttempts, Backoff backoff)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAttempts, 0, nameof(maxAttempts));
        ArgumentNullException.ThrowIfNull(backoff);

        if (retryConfigured)
        {
            throw new InvalidOperationException("Retry has already been configured.");
        }

        EnsureTerminalNotSet();
        retryConfigured = true;
        SetRetry(maxAttempts, backoff);
        return this;
    }
}

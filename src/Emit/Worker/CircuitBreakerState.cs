namespace Emit.Worker;

/// <summary>
/// Represents the state of a circuit breaker.
/// </summary>
internal enum CircuitState
{
    /// <summary>
    /// Circuit is closed - requests are allowed through.
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open - requests are blocked.
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is half-open - one request is allowed through to test.
    /// </summary>
    HalfOpen
}

/// <summary>
/// Represents the state of a circuit breaker for a specific group.
/// </summary>
/// <remarks>
/// The circuit breaker prevents repeated failures from overwhelming
/// the system by temporarily blocking requests to failing groups.
/// </remarks>
internal sealed class CircuitBreakerState
{
    /// <summary>
    /// Gets or sets the current state of the circuit breaker.
    /// </summary>
    public CircuitState State { get; set; } = CircuitState.Closed;

    /// <summary>
    /// Gets or sets the number of consecutive failures.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Gets or sets when the circuit breaker was opened.
    /// </summary>
    public DateTime? OpenedAt { get; set; }
}

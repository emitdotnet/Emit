namespace Emit.Pipeline.Modules;

using Emit.Consumer;

/// <summary>
/// Holds circuit breaker configuration. Delegates to the existing <see cref="CircuitBreakerBuilder"/>.
/// </summary>
public sealed class CircuitBreakerModule
{
    private Action<CircuitBreakerBuilder>? configure;

    /// <summary>
    /// Gets whether the circuit breaker has been configured.
    /// </summary>
    public bool IsConfigured => configure is not null;

    /// <summary>
    /// Configures the circuit breaker.
    /// </summary>
    /// <param name="configure">Action to configure the circuit breaker builder.</param>
    /// <exception cref="InvalidOperationException">Circuit breaker has already been configured.</exception>
    public void Configure(Action<CircuitBreakerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (IsConfigured)
        {
            throw new InvalidOperationException("Circuit breaker has already been configured.");
        }

        this.configure = configure;
    }

    /// <summary>
    /// Builds the circuit breaker config, or <c>null</c> if not configured.
    /// </summary>
    internal CircuitBreakerConfig? BuildConfig()
    {
        if (configure is null)
        {
            return null;
        }

        var builder = new CircuitBreakerBuilder();
        configure(builder);
        return builder.Build();
    }
}

namespace Emit.DependencyInjection;

using Emit.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering observer types on the <see cref="EmitBuilder"/>.
/// </summary>
public static class EmitBuilderObserverExtensions
{
    /// <summary>
    /// Registers a produce observer that is notified before, after, and on failure of
    /// every outbound (produce) operation across all transport providers.
    /// </summary>
    /// <typeparam name="T">The observer type.</typeparam>
    /// <param name="builder">The Emit builder.</param>
    /// <returns>The Emit builder for method chaining.</returns>
    public static EmitBuilder AddProduceObserver<T>(this EmitBuilder builder) where T : class, IProduceObserver
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProduceObserver, T>();
        return builder;
    }

    /// <summary>
    /// Registers a consume observer that is notified before, after, and on failure of
    /// every inbound (consume) operation across all transport providers. Does not fire
    /// for mediator requests.
    /// </summary>
    /// <typeparam name="T">The observer type.</typeparam>
    /// <param name="builder">The Emit builder.</param>
    /// <returns>The Emit builder for method chaining.</returns>
    public static EmitBuilder AddConsumeObserver<T>(this EmitBuilder builder) where T : class, IConsumeObserver
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IConsumeObserver, T>();
        return builder;
    }

    /// <summary>
    /// Registers an outbox observer that is notified on outbox entry enqueue, processing,
    /// completion, and failure.
    /// </summary>
    /// <typeparam name="T">The observer type.</typeparam>
    /// <param name="builder">The Emit builder.</param>
    /// <returns>The Emit builder for method chaining.</returns>
    public static EmitBuilder AddOutboxObserver<T>(this EmitBuilder builder) where T : class, IOutboxObserver
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IOutboxObserver, T>();
        return builder;
    }

    /// <summary>
    /// Registers a leader election observer that is notified when this node is elected or loses leadership, and when
    /// nodes of its service register or are removed.
    /// </summary>
    /// <typeparam name="T">The observer type.</typeparam>
    /// <param name="builder">The Emit builder.</param>
    /// <returns>The Emit builder for method chaining.</returns>
    public static EmitBuilder AddLeaderElectionObserver<T>(this EmitBuilder builder) where T : class, ILeaderElectionObserver
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ILeaderElectionObserver, T>();
        return builder;
    }

    /// <summary>
    /// Registers a daemon observer that is notified when daemons are assigned, started, stopped and revoked.
    /// </summary>
    /// <typeparam name="T">The observer type.</typeparam>
    /// <param name="builder">The Emit builder.</param>
    /// <returns>The Emit builder for method chaining.</returns>
    public static EmitBuilder AddDaemonObserver<T>(this EmitBuilder builder) where T : class, IDaemonObserver
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IDaemonObserver, T>();
        return builder;
    }
}

namespace Emit.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.Pipeline;
using Emit.Configuration;
using Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Builder for configuring Emit services.
/// </summary>
public sealed class EmitBuilder : IInboundConfigurable, IOutboundConfigurable
{
    private readonly IServiceCollection services;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmitBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal EmitBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    /// <summary>
    /// Gets the service collection for registering provider-specific services.
    /// </summary>
    public IServiceCollection Services => services;

    /// <summary>
    /// Gets the global inbound middleware pipeline builder. Middleware registered here wraps
    /// every inbound message regardless of pattern (Kafka, Mediator, Bus).
    /// </summary>
    public IMessagePipelineBuilder InboundPipeline { get; } = new MessagePipelineBuilder();

    /// <summary>
    /// Gets the global outbound middleware pipeline builder. Middleware registered here wraps
    /// every outbound message regardless of pattern (Kafka, Mediator, Bus).
    /// </summary>
    public IMessagePipelineBuilder OutboundPipeline { get; } = new MessagePipelineBuilder();

    /// <summary>
    /// Gets a value indicating whether outbox mode is enabled.
    /// </summary>
    /// <remarks>
    /// When <c>true</c>, producers enqueue messages to the transactional outbox and
    /// background workers deliver them. When <c>false</c>, producers send directly
    /// to the external system.
    /// <para>
    /// Deliberately internal. The value is only final once every integration has been
    /// registered, so exposing it during configuration would let an integration branch on a
    /// provisional answer that depends on registration order. Integrations resolve the state
    /// from a built provider instead, via <see cref="OutboxRegistrationExtensions.IsOutboxEnabled"/>.
    /// </para>
    /// </remarks>
    internal bool OutboxEnabled => services.Any(d => d.ImplementationInstance is OutboxRegistrationMarker);

    /// <summary>
    /// Configures leader election interval options.
    /// </summary>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Calling this method is optional. Leader election auto-activates when a persistence
    /// provider is registered; these options only override the default intervals.
    /// </remarks>
    public EmitBuilder ConfigureLeaderElection(Action<LeaderElectionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Configures daemon assignment options.
    /// </summary>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Calling this method is optional. Daemon coordination auto-activates when a persistence
    /// provider is registered; these options only override the default timeouts.
    /// </remarks>
    public EmitBuilder ConfigureDaemons(Action<DaemonOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Registers a daemon: work that runs on exactly one node of the service at a time, and moves to another node
    /// when that node goes away. The daemon is a singleton, also resolvable as <typeparamref name="TDaemon"/>.
    /// </summary>
    /// <typeparam name="TDaemon">The daemon type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Daemons need a persistence provider, through which the leader of the service assigns them to nodes. Every
    /// replica of the service must register the same daemons. Each daemon needs a unique
    /// <see cref="IDaemonAgent.DaemonId"/> that stays the same across deployments; identifiers starting with
    /// <c>emit:</c> are reserved.
    /// </para>
    /// <para>
    /// The daemon lives as long as the application. To use scoped services, such as a database context, inject
    /// <see cref="IServiceScopeFactory"/> and create a scope for each unit of work.
    /// </para>
    /// </remarks>
    public EmitBuilder AddDaemon<TDaemon>() where TDaemon : class, IDaemonAgent
    {
        // Typed on the daemon, so an error about the registration can name it
        Func<IServiceProvider, TDaemon> resolve = sp => sp.GetRequiredService<TDaemon>();
        services.TryAddSingleton<TDaemon>();
        services.AddSingleton<IDaemonAgent>(resolve);
        return this;
    }

    /// <summary>
    /// Registers a daemon created by <paramref name="factory"/>: work that runs on exactly one node of the service at
    /// a time, and moves to another node when that node goes away. The daemon is a singleton. One type can back
    /// several daemons this way, each with its own <see cref="IDaemonAgent.DaemonId"/>.
    /// </summary>
    /// <typeparam name="TDaemon">The daemon type.</typeparam>
    /// <param name="factory">Creates the daemon.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// The same rules apply as for <see cref="AddDaemon{TDaemon}()"/>.
    /// </remarks>
    public EmitBuilder AddDaemon<TDaemon>(Func<IServiceProvider, TDaemon> factory) where TDaemon : class, IDaemonAgent
    {
        ArgumentNullException.ThrowIfNull(factory);

        services.AddSingleton<IDaemonAgent>(factory);
        return this;
    }

    /// <summary>
    /// Configures distributed tracing options.
    /// </summary>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public EmitBuilder ConfigureTracing(Action<EmitTracingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EmitTracingBuilder(services);
        configure(builder);
        return this;
    }

    /// <summary>
    /// Validates the builder configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if outbox mode is enabled without an outbox provider, or if the outbox or
    /// distributed lock is registered by more than one persistence provider.
    /// </exception>
    internal void Validate()
    {
        var persistenceProviders = services
            .Select(d => d.ImplementationInstance)
            .OfType<PersistenceProviderMarker>()
            .Select(m => m.ProviderName)
            .Distinct()
            .ToList();

        if (persistenceProviders is [var firstProvider, var secondProvider, ..])
        {
            throw new InvalidOperationException(
                $"Multiple persistence providers registered: {firstProvider}, {secondProvider}. " +
                "Only one persistence provider is allowed.");
        }

        var outboxProviders = services
            .Select(d => d.ImplementationInstance)
            .OfType<OutboxRegistrationMarker>()
            .Select(m => m.ProviderName)
            .Distinct()
            .ToList();

        if (outboxProviders is [var firstOutbox, var secondOutbox, ..])
        {
            throw new InvalidOperationException(
                $"The outbox is registered by multiple persistence providers: {firstOutbox}, {secondOutbox}. " +
                "Only one persistence provider can register the outbox.");
        }

        var lockProviders = services
            .Select(d => d.ImplementationInstance)
            .OfType<DistributedLockRegistrationMarker>()
            .Select(m => m.ProviderName)
            .Distinct()
            .ToList();

        if (lockProviders is [var firstLock, var secondLock, ..])
        {
            throw new InvalidOperationException(
                $"The distributed lock is registered by multiple persistence providers: {firstLock}, {secondLock}. " +
                "Only one persistence provider can register the distributed lock.");
        }

        // Daemons are coordinated through the persistence provider's database; without one nothing would run them
        var daemons = services
            .Where(d => !d.IsKeyedService && d.ServiceType == typeof(IDaemonAgent))
            .Select(d => (d.ImplementationType ?? d.ImplementationInstance?.GetType() ?? d.ImplementationFactory?.Method.ReturnType)?.Name)
            .Distinct()
            .ToList();

        if (daemons.Count > 0 && persistenceProviders is [])
        {
            throw new InvalidOperationException(
                $"Daemons are registered ({string.Join(", ", daemons)}) but no persistence provider is. " +
                "Daemons are assigned to nodes through the persistence provider's database, so they would never run. " +
                "Register a persistence provider.");
        }

        if (OutboxEnabled && !services.Any(d => d.ImplementationInstance is OutboxProviderMarker))
        {
            throw new InvalidOperationException(
                "No outbox provider has been registered. " +
                "Outbox mode requires at least one outbox provider.");
        }

        // A handler decorated with [Transactional] needs a unit of work to open a transaction on,
        // and only a persistence provider supplies one. Silently dropping the transaction would
        // leave the handler believing its writes are atomic when they are not, so this is rejected
        // here, after every integration has registered, rather than left to fail at request time.
        var transactionalHandlers = services
            .Select(d => d.ImplementationInstance)
            .OfType<TransactionalHandlerMarker>()
            .Select(m => m.HandlerType.Name)
            .Distinct()
            .ToList();

        if (transactionalHandlers.Count > 0 && !services.Any(d => d.ServiceType == typeof(IUnitOfWork)))
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", transactionalHandlers)} decorated with " +
                $"[{nameof(TransactionalAttribute)}] but no {nameof(IUnitOfWork)} is registered. " +
                "Add a persistence provider, or remove the attribute.");
        }
    }
}

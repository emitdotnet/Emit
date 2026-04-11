namespace Emit.DependencyInjection;

using Emit.Abstractions.Pipeline;
using Emit.Configuration;
using Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;

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
    /// </remarks>
    public bool OutboxEnabled => services.Any(d => d.ImplementationInstance is OutboxRegistrationMarker);

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

        if (OutboxEnabled && !services.Any(d => d.ImplementationInstance is OutboxProviderMarker))
        {
            throw new InvalidOperationException(
                "No outbox provider has been registered. " +
                "Outbox mode requires at least one outbox provider.");
        }
    }
}

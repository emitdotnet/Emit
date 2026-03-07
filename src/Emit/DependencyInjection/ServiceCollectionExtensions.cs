namespace Emit.DependencyInjection;

using System.Diagnostics.Metrics;
using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Abstractions.Metrics;
using Emit.Configuration;
using Emit.Daemon;
using Emit.LeaderElection;
using Emit.Metrics;
using Emit.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Extension methods for adding Emit services to the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Emit services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration action for the Emit builder.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if Emit has already been registered, or if the outbox is enabled
    /// without an outbox provider, or if the outbox or distributed lock is registered
    /// by more than one persistence provider.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Enable the transactional outbox by calling <c>UseOutbox()</c> inside a persistence
    /// provider builder. Without it, producers send directly to the external system.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEmit(
        this IServiceCollection services,
        Action<EmitBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Prevent double registration
        if (services.Any(d => d.ServiceType == typeof(EmitMarkerService)))
        {
            throw new InvalidOperationException(
                $"{nameof(AddEmit)} has already been called. Emit services should only be registered once.");
        }

        services.AddSingleton<EmitMarkerService>();

        // Register infrastructure dependencies
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRandomProvider, DefaultRandomProvider>();

        // Register metrics infrastructure
        services.TryAddSingleton(new EmitMetricsEnrichment());
        services.TryAddSingleton(sp => new LockMetrics(
            sp.GetService<IMeterFactory>(),
            sp.GetRequiredService<EmitMetricsEnrichment>()));
        services.TryAddSingleton(sp => new OutboxMetrics(
            sp.GetService<IMeterFactory>(),
            sp.GetRequiredService<EmitMetricsEnrichment>()));
        services.TryAddSingleton(sp => new EmitMetrics(
            sp.GetService<IMeterFactory>(),
            sp.GetRequiredService<EmitMetricsEnrichment>()));

        // Register scoped EmitContext
        services.AddScoped<EmitContext>();
        services.AddScoped<IEmitContext>(sp => sp.GetRequiredService<EmitContext>());

        var builder = new EmitBuilder(services);

        // Register tracing infrastructure
        services.AddSingleton<IValidateOptions<Tracing.EmitTracingOptions>, Tracing.EmitTracingOptionsValidator>();
        services.AddOptions<Tracing.EmitTracingOptions>().ValidateOnStart();
        services.AddScoped<Tracing.ActivityEnricherInvoker>();

        // Auto-insert tracing middleware first (outermost), then metrics, then observers
        builder.OutboundPipeline.Use(typeof(Tracing.ProduceTracingMiddleware<>));
        builder.InboundPipeline.Use(typeof(Tracing.ConsumeTracingMiddleware<>));

        builder.OutboundPipeline.Use(typeof(ProduceMetricsMiddleware<>));
        builder.OutboundPipeline.Use(typeof(ProduceObserverMiddleware<>));
        builder.InboundPipeline.Use(typeof(ConsumeMetricsMiddleware<>));
        builder.InboundPipeline.Use(typeof(ConsumeObserverMiddleware<>));

        configure(builder);
        builder.Validate();

        // Register global middleware types with appropriate lifetimes
        builder.InboundPipeline.RegisterServices(services);
        builder.OutboundPipeline.RegisterServices(services);

        // Register outbox observer invoker (zero overhead when no observers registered)
        services.AddSingleton<OutboxObserverInvoker>();

        // Register outbox-specific services only when outbox mode is enabled
        if (builder.OutboxEnabled)
        {
            // Options are registered by the persistence provider; we only add validation here
            services.AddOptions<OutboxOptions>().ValidateOnStart();
            services.AddSingleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>();

            services.AddSingleton<OutboxDaemon>();
            services.AddSingleton<IDaemonAgent>(sp => sp.GetRequiredService<OutboxDaemon>());
        }

        // Register leader election and daemon coordination when a persistence provider is present
        if (services.Any(d => d.ImplementationInstance is PersistenceProviderMarker))
        {
            // Options are registered by ConfigureLeaderElection/ConfigureDaemons; we only add validation here
            services.AddOptions<LeaderElectionOptions>().ValidateOnStart();
            services.AddSingleton<IValidateOptions<LeaderElectionOptions>, LeaderElectionOptionsValidator>();
            services.AddOptions<DaemonOptions>().ValidateOnStart();
            services.AddSingleton<IValidateOptions<DaemonOptions>, DaemonOptionsValidator>();

            services.AddSingleton<LeaderElectionObserverInvoker>();
            services.AddSingleton<DaemonObserverInvoker>();
            services.AddSingleton<DaemonCoordinator>();
            services.AddSingleton<HeartbeatWorker>();
            services.AddSingleton<ILeaderElectionService>(sp => sp.GetRequiredService<HeartbeatWorker>());
            services.AddHostedService(sp => sp.GetRequiredService<HeartbeatWorker>());
        }

        return services;
    }

    private sealed class EmitMarkerService;
}

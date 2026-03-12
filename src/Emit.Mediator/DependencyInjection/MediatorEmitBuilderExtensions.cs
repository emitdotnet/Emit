namespace Emit.Mediator.DependencyInjection;

using System.Diagnostics.Metrics;
using System.Reflection;
using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Mediator.Metrics;
using Emit.Mediator.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for adding mediator services to the Emit builder.
/// </summary>
public static class MediatorEmitBuilderExtensions
{
    private static readonly MethodInfo BuildDispatcherMethod =
        typeof(MediatorEmitBuilderExtensions).GetMethod(nameof(BuildDispatcher), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Adds the mediator pattern to Emit. Registers request handlers and the
    /// <see cref="IMediator"/> service for in-process request-response dispatching.
    /// </summary>
    /// <param name="builder">The Emit builder.</param>
    /// <param name="configure">The configuration action for registering handlers.</param>
    /// <returns>The Emit builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the mediator has already been registered.
    /// </exception>
    public static EmitBuilder AddMediator(this EmitBuilder builder, Action<MediatorBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        if (services.Any(d => d.ServiceType == typeof(MediatorMarkerService)))
        {
            throw new InvalidOperationException(
                $"{nameof(AddMediator)} has already been called. The mediator should only be registered once.");
        }

        services.AddSingleton<MediatorMarkerService>();

        // Register mediator metrics
        services.TryAddSingleton(sp => new MediatorMetrics(
            sp.GetService<IMeterFactory>(),
            sp.GetRequiredService<EmitMetricsEnrichment>()));

        var mediatorBuilder = new MediatorBuilder();

        // Auto-insert metrics middleware as outermost, then observer middleware
        mediatorBuilder.InboundPipeline.Use(typeof(MediatorMetricsMiddleware<>));
        mediatorBuilder.InboundPipeline.Use(typeof(MediatorObserverMiddleware<>));

        configure(mediatorBuilder);

        // Register observer types from builder
        foreach (var observerType in mediatorBuilder.ObserverTypes)
        {
            services.AddSingleton(typeof(IMediatorObserver), observerType);
        }

        // Register each handler type as scoped
        foreach (var registration in mediatorBuilder.Registrations.Values)
        {
            services.TryAddScoped(registration.HandlerType);
        }

        // Register mediator middleware with appropriate lifetimes
        mediatorBuilder.InboundPipeline.RegisterServices(services);

        // Build the invoker map at registration time — stores IHandlerInvoker<TRequest> as object
        var invokerMap = new Dictionary<Type, object>();
        var handlerPipelineMap = new Dictionary<Type, IMessagePipelineBuilder>();
        foreach (var registration in mediatorBuilder.Registrations.Values)
        {
            var invokerType = registration.ResponseType is not null
                ? typeof(MediatorHandlerInvoker<,>).MakeGenericType(registration.RequestType, registration.ResponseType)
                : typeof(MediatorVoidHandlerInvoker<>).MakeGenericType(registration.RequestType);

            var invoker = Activator.CreateInstance(invokerType, registration.HandlerType)!;
            invokerMap[registration.RequestType] = invoker;

            if (registration.HandlerPipeline is not null)
            {
                registration.HandlerPipeline.RegisterServices(services);
                handlerPipelineMap[registration.RequestType] = registration.HandlerPipeline;
            }
        }

        // Capture pipeline references for the closure
        var globalInbound = builder.InboundPipeline;
        var mediatorInbound = mediatorBuilder.InboundPipeline;

        // Factory-built singleton — typed pipelines composed when IServiceProvider available.
        // Each request type gets a typed dispatch delegate that creates MediatorContext<TRequest>
        // and invokes the typed pipeline with no runtime reflection.
        services.AddSingleton(sp =>
        {
            var dispatchers = new Dictionary<Type, Func<object, IServiceProvider, TimeProvider, CancellationToken, MediatorResponseFeature?, Task>>();

            foreach (var (requestType, invoker) in invokerMap)
            {
                handlerPipelineMap.TryGetValue(requestType, out var handlerPipeline);
                var genericMethod = BuildDispatcherMethod.MakeGenericMethod(requestType);
                var dispatcher = (Func<object, IServiceProvider, TimeProvider, CancellationToken, MediatorResponseFeature?, Task>)
                    genericMethod.Invoke(null, [sp, invoker, mediatorInbound, globalInbound, handlerPipeline])!;
                dispatchers[requestType] = dispatcher;
            }

            return new MediatorConfiguration(dispatchers);
        });

        services.AddScoped<IMediator, Mediator>();

        return builder;
    }

    /// <summary>
    /// Generic helper invoked via reflection (<see cref="BuildDispatcherMethod"/>)
    /// to build a typed dispatch delegate for a specific request type.
    /// </summary>
    private static Func<object, IServiceProvider, TimeProvider, CancellationToken, MediatorResponseFeature?, Task>
        BuildDispatcher<TRequest>(
            IServiceProvider sp,
            object invoker,
            IMessagePipelineBuilder mediatorInbound,
            IMessagePipelineBuilder globalInbound,
            IMessagePipelineBuilder? handlerPipeline)
    {
        var typedInvoker = (IHandlerInvoker<MediatorContext<TRequest>>)invoker;

        // Pipeline layering: global → mediator → per-handler → terminal
        var pipeline = handlerPipeline is not null
            ? handlerPipeline.Build<MediatorContext<TRequest>, TRequest>(sp, typedInvoker, globalInbound, mediatorInbound)
            : mediatorInbound.Build<MediatorContext<TRequest>, TRequest>(sp, typedInvoker, globalInbound);

        return (request, services, timeProvider, ct, responseFeature) =>
        {
            var context = new MediatorContext<TRequest>
            {
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = timeProvider.GetUtcNow(),
                CancellationToken = ct,
                Services = services,
                Message = (TRequest)request,
            };

            if (responseFeature is not null)
                context.Features.Set<IResponseFeature>(responseFeature);

            return pipeline.InvokeAsync(context);
        };
    }

    private sealed class MediatorMarkerService;
}

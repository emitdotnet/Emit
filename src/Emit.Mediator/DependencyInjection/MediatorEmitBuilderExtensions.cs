namespace Emit.Mediator.DependencyInjection;

using System.Diagnostics.Metrics;
using System.Reflection;
using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using Emit.DependencyInjection;
using Emit.Mediator.Metrics;
using Emit.Mediator.Observability;
using Emit.Mediator.Tracing;
using Emit.Middleware;
using Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
        services.AddSingleton(new PackageMeter(MediatorMeterNames.Mediator));

        // Built-in middleware, composed around every request's pipeline by BuildDispatcher
        services.TryAddSingleton(typeof(MediatorTracingMiddleware<>));
        services.TryAddSingleton(typeof(MediatorMetricsMiddleware<>));
        services.TryAddSingleton(typeof(MediatorObserverMiddleware<>));

        var mediatorBuilder = new MediatorBuilder();
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

            if (Attribute.IsDefined(registration.HandlerType, typeof(TransactionalAttribute)))
            {
                services.AddSingleton(new TransactionalHandlerMarker(registration.HandlerType));
            }
        }

        // Register mediator middleware with appropriate lifetimes
        mediatorBuilder.PipelineLevel.RegisterServices(services);

        // Build the invoker map at registration time — stores IMiddlewarePipeline<TRequest> as object
        var invokerMap = new Dictionary<Type, object>();
        var handlerPipelineMap = new Dictionary<Type, MessagePipelineBuilder>();
        foreach (var registration in mediatorBuilder.Registrations.Values)
        {
            var invokerType = registration switch
            {
                { IsStream: true, ResponseType: { } streamResponse } =>
                    typeof(MediatorStreamHandlerInvoker<,>).MakeGenericType(registration.RequestType, streamResponse),
                { ResponseType: { } response } =>
                    typeof(MediatorHandlerInvoker<,>).MakeGenericType(registration.RequestType, response),
                _ => typeof(MediatorVoidHandlerInvoker<>).MakeGenericType(registration.RequestType),
            };

            var invoker = Activator.CreateInstance(invokerType, registration.HandlerType)!;
            invokerMap[registration.RequestType] = invoker;

            if (registration.HandlerPipeline is not null)
            {
                registration.HandlerPipeline.RegisterServices(services);
                handlerPipelineMap[registration.RequestType] = registration.HandlerPipeline;
            }
        }

        // Capture pipeline references for the closure
        var globalPipeline = builder.PipelineLevel;
        var mediatorPipeline = mediatorBuilder.PipelineLevel;

        // Factory-built singleton — typed pipelines composed when IServiceProvider available.
        // Each request type gets a typed dispatch delegate that creates MediatorContext<TRequest>
        // and invokes the typed pipeline with no runtime reflection.
        services.AddSingleton(sp =>
        {
            var dispatchers = new Dictionary<Type, MediatorDispatch>();

            foreach (var (requestType, invoker) in invokerMap)
            {
                handlerPipelineMap.TryGetValue(requestType, out var handlerPipeline);
                var registration = mediatorBuilder.Registrations[requestType];

                // Stream and non-stream requests are composed by the same code path. The only
                // difference is the terminal, already chosen above, and the feature the caller
                // seeds onto the context to receive its result.
                var genericMethod = BuildDispatcherMethod.MakeGenericMethod(requestType);
                var dispatch = (MediatorDispatch)genericMethod.Invoke(
                    null,
                    [sp, invoker, globalPipeline, mediatorPipeline, handlerPipeline, registration.HandlerType])!;

                dispatchers[requestType] = dispatch;
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
    private static MediatorDispatch
        BuildDispatcher<TRequest>(
            IServiceProvider sp,
            object invoker,
            MessagePipelineBuilder globalPipeline,
            MessagePipelineBuilder mediatorPipeline,
            MessagePipelineBuilder? handlerPipeline,
            Type handlerType)
    {
        IMiddlewarePipeline<MediatorContext<TRequest>> terminal = (IMiddlewarePipeline<MediatorContext<TRequest>>)invoker;

        // The handler's own middleware runs inside its unit of work, so nothing it does can fail
        // after the commit.
        terminal = MessagePipelineBuilder.Build<MediatorContext<TRequest>, TRequest>(sp, terminal, handlerPipeline);

        var unitOfWorkMw = new UnitOfWorkMiddleware<MediatorContext<TRequest>>(
            handlerType,
            sp.GetRequiredService<ILogger<UnitOfWorkMiddleware<MediatorContext<TRequest>>>>());
        terminal = new MiddlewarePipeline<MediatorContext<TRequest>>(unitOfWorkMw, terminal);

        // Pipeline layering: tracing → metrics → observers → every pipeline → mediator → unit of work → per-handler
        // → terminal
        var pipeline = MessagePipelineBuilder.Build<MediatorContext<TRequest>, TRequest>(
            sp, terminal, globalPipeline, mediatorPipeline);
        pipeline = new MiddlewarePipeline<MediatorContext<TRequest>>(
            sp.GetRequiredService<MediatorObserverMiddleware<TRequest>>(), pipeline);
        pipeline = new MiddlewarePipeline<MediatorContext<TRequest>>(
            sp.GetRequiredService<MediatorMetricsMiddleware<TRequest>>(), pipeline);
        pipeline = new MiddlewarePipeline<MediatorContext<TRequest>>(
            sp.GetRequiredService<MediatorTracingMiddleware<TRequest>>(), pipeline);

        return (request, services, timeProvider, ct, configureFeatures) =>
        {
            var context = new MediatorContext<TRequest>
            {
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = timeProvider.GetUtcNow(),
                CancellationToken = ct,
                Services = services,
                Message = (TRequest)request,
            };

            configureFeatures?.Invoke(context.Features);

            return pipeline.InvokeAsync(context);
        };
    }

    private sealed class MediatorMarkerService;
}

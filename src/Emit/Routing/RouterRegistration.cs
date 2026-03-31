namespace Emit.Routing;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Middleware;
using Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Immutable descriptor capturing the build-time state of a message router.
/// Returned by <see cref="MessageRouterBuilder{TMessage, TRouteKey}.Build"/>.
/// Provides a <see cref="BuildInvoker"/> method to construct the router invoker
/// at service resolution time.
/// </summary>
public sealed class RouterRegistration<TMessage>
{
    private readonly Func<ConsumeContext<TMessage>, object?> routeSelector;
    private readonly IReadOnlyList<RouteEntry<TMessage>> routes;

    internal RouterRegistration(
        Func<ConsumeContext<TMessage>, object?> routeSelector,
        IReadOnlyList<RouteEntry<TMessage>> routes)
    {
        this.routeSelector = routeSelector;
        this.routes = routes;
        ConsumerTypes = routes.Select(r => r.ConsumerType).ToList();
    }

    /// <summary>
    /// All consumer handler types registered as routes in this router.
    /// </summary>
    public IReadOnlyList<Type> ConsumerTypes { get; }

    /// <summary>
    /// The user-provided identifier for this router.
    /// </summary>
    public string Identifier { get; set; } = default!;

    /// <summary>
    /// Builds the router invoker, assembling sub-pipelines for each route.
    /// </summary>
    /// <param name="terminalFactory">
    /// Creates a terminal pipeline for each route's consumer type.
    /// The provider supplies this (e.g., Kafka creates <c>HandlerInvoker</c>).
    /// </param>
    /// <param name="services">The service provider for resolving middleware.</param>
    /// <param name="loggerFactory">Logger factory for the router invoker.</param>
    /// <param name="unmatchedAction">
    /// Pre-evaluated error action for unmatched messages. When <see cref="ErrorAction.DiscardAction"/>,
    /// the invoker discards unmatched messages inline without throwing. Pass <c>null</c> to always throw.
    /// </param>
    /// <param name="outboxEnabled">
    /// When <see langword="true"/>, each route's terminal is wrapped with
    /// <see cref="TransactionalOutboxMiddleware{TContext}"/> so that handlers decorated with
    /// <see cref="TransactionalAttribute"/> participate in a unit of work.
    /// </param>
    /// <returns>The constructed router pipeline ready to participate in fan-out.</returns>
    public IMiddlewarePipeline<ConsumeContext<TMessage>> BuildInvoker(
        Func<Type, IMiddlewarePipeline<ConsumeContext<TMessage>>> terminalFactory,
        IServiceProvider services,
        ILoggerFactory loggerFactory,
        ErrorAction? unmatchedAction = null,
        bool outboxEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(terminalFactory);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var subPipelines = new Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<TMessage>> Pipeline)>();
        foreach (var route in routes)
        {
            IMiddlewarePipeline<ConsumeContext<TMessage>> subPipeline = terminalFactory(route.ConsumerType);
            if (outboxEnabled)
            {
                var transactionalMw = new TransactionalOutboxMiddleware<ConsumeContext<TMessage>>(route.ConsumerType);
                subPipeline = new MiddlewarePipeline<ConsumeContext<TMessage>>(transactionalMw, subPipeline);
            }
            if (route.Pipeline is not null)
                subPipeline = route.Pipeline.Build<ConsumeContext<TMessage>, TMessage>(services, subPipeline);
            subPipelines[route.RouteKey] = (route.ConsumerType, subPipeline);
        }

        return new MessageRouterInvoker<TMessage>(
            Identifier,
            routeSelector,
            subPipelines,
            loggerFactory.CreateLogger<MessageRouterInvoker<TMessage>>(),
            unmatchedAction);
    }

    /// <summary>
    /// Registers the consumer types and per-route middleware services in the service collection.
    /// </summary>
    /// <param name="services">The service collection to register in.</param>
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var route in routes)
        {
            services.TryAddScoped(route.ConsumerType);
            route.Pipeline?.RegisterServices(services);
        }
    }
}

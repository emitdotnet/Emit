namespace Emit.Routing;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Pipeline;
using Emit.Pipeline.Modules;

/// <summary>
/// Internal <see cref="IInboundConfigurable{TMessage}"/> implementation used inside
/// per-route callbacks. Provides <c>Use</c> and <c>Filter</c> without exposing
/// group-level or router-level methods.
/// </summary>
internal sealed class RouteHandlerConfigurator<TMessage> : IInboundConfigurable<TMessage>
{
    /// <inheritdoc />
    IMessagePipelineBuilder IInboundConfigurable.InboundPipeline => Pipeline;

    /// <summary>
    /// Gets the per-route middleware pipeline builder.
    /// </summary>
    internal IMessagePipelineBuilder Pipeline { get; } = new MessagePipelineBuilder();

    /// <inheritdoc />
    public IInboundConfigurable<TMessage> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<ConsumeContext<TMessage>>
    {
        Pipeline.Use(typeof(TMiddleware), lifetime);
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TMessage> Filter<TFilter>()
        where TFilter : class, IConsumerFilter<TMessage>
    {
        Pipeline.AddConsumerFilter<TMessage, TFilter>();
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TMessage> Filter(
        Func<ConsumeContext<TMessage>, CancellationToken, ValueTask<bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var middleware = new FilterMiddleware<TMessage>();
        middleware.AddPredicate(predicate);
        Pipeline.Use(_ => middleware, MiddlewareLifetime.Singleton);
        return this;
    }
}

namespace Emit.Routing;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Pipeline;
using Emit.Pipeline.Modules;

/// <summary>
/// Internal <see cref="IConsumerConfigurable{TMessage}"/> implementation used inside
/// per-route callbacks. Provides <c>Use</c>, <c>Filter</c> and <c>UseInbox</c> without exposing
/// group-level or router-level methods.
/// </summary>
internal sealed class RouteHandlerConfigurator<TMessage> : IConsumerConfigurable<TMessage>
{
    /// <summary>
    /// Gets the per-route middleware level.
    /// </summary>
    internal MessagePipelineBuilder<ConsumeContext<TMessage>> Pipeline { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the route's consumer uses the inbox.
    /// </summary>
    internal bool InboxEnabled { get; private set; }

    /// <inheritdoc />
    public IConsumerConfigurable<TMessage> UseInbox()
    {
        InboxEnabled = true;
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TMessage> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<ConsumeContext<TMessage>>
    {
        Pipeline.Use<TMiddleware>(lifetime);
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TMessage> Use(
        Func<IServiceProvider, IMiddleware<ConsumeContext<TMessage>>> factory,
        MiddlewareLifetime lifetime = default)
    {
        Pipeline.Use(factory, lifetime);
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

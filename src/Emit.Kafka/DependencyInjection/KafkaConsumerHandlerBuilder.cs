namespace Emit.Kafka.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Pipeline;
using Emit.Pipeline.Modules;

/// <summary>
/// Configures per-consumer middleware and the inbox for a single consumer handler registered
/// via <see cref="KafkaConsumerGroupBuilder{TKey, TValue}.AddConsumer{TConsumer}(Action{KafkaConsumerHandlerBuilder{TValue}})"/>.
/// Middleware registered here wraps only this consumer's invocation, running after
/// any group-level middleware.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class KafkaConsumerHandlerBuilder<TValue> : IConsumerConfigurable<TValue>
{
    /// <summary>
    /// Gets the per-consumer middleware level.
    /// </summary>
    internal MessagePipelineBuilder<ConsumeContext<TValue>> Pipeline { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the consumer uses the inbox.
    /// </summary>
    internal bool InboxEnabled { get; private set; }

    /// <inheritdoc />
    public IConsumerConfigurable<TValue> UseInbox()
    {
        InboxEnabled = true;
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TValue> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<ConsumeContext<TValue>>
    {
        Pipeline.Use<TMiddleware>(lifetime);
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TValue> Use(
        Func<IServiceProvider, IMiddleware<ConsumeContext<TValue>>> factory,
        MiddlewareLifetime lifetime = default)
    {
        Pipeline.Use(factory, lifetime);
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TValue> Filter<TFilter>()
        where TFilter : class, IConsumerFilter<TValue>
    {
        Pipeline.AddConsumerFilter<TValue, TFilter>();
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TValue> Filter(
        Func<ConsumeContext<TValue>, CancellationToken, ValueTask<bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var middleware = new FilterMiddleware<TValue>();
        middleware.AddPredicate(predicate);
        Pipeline.Use(_ => middleware, MiddlewareLifetime.Singleton);
        return this;
    }
}

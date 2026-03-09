namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering consumer filters on inbound pipelines.
/// </summary>
public static class InboundFilterExtensions
{
    /// <summary>
    /// Adds a class-based consumer filter to the pipeline builder. The filter is resolved from
    /// the scoped service provider for each message. If <typeparamref name="TFilter"/> is
    /// registered in DI, it is resolved from there; otherwise, a new instance is created
    /// with constructor injection.
    /// </summary>
    /// <remarks>
    /// This helper is intended for implementors of <see cref="IInboundConfigurable{TMessage}"/>
    /// to delegate their <see cref="IInboundConfigurable{TMessage}.Filter{TFilter}"/> implementation.
    /// </remarks>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <param name="pipeline">The pipeline builder to add the filter to.</param>
    public static void AddConsumerFilter<TMessage, TFilter>(this IMessagePipelineBuilder pipeline)
        where TFilter : class, IConsumerFilter<TMessage>
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        pipeline.Use(
            _ => new ConsumerFilterMiddleware<TMessage>(
                (context, ct) =>
                {
                    var filter = ActivatorUtilities.GetServiceOrCreateInstance<TFilter>(context.Services);
                    return filter.ShouldConsumeAsync(context, ct);
                }),
            MiddlewareLifetime.Singleton);
    }

    /// <summary>
    /// Registers a synchronous predicate filter. When the predicate returns <c>false</c>,
    /// the pipeline is short-circuited and the consumer handler is not invoked.
    /// </summary>
    /// <typeparam name="TMessage">The message type, inferred from the builder.</typeparam>
    /// <param name="builder">The inbound builder to register the filter on.</param>
    /// <param name="predicate">
    /// A predicate that receives the consume context. Return <c>true</c> to continue
    /// the pipeline, <c>false</c> to skip.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    public static IInboundConfigurable<TMessage> Filter<TMessage>(
        this IInboundConfigurable<TMessage> builder,
        Func<ConsumeContext<TMessage>, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(predicate);

        return builder.Use(
            _ => new ConsumerFilterMiddleware<TMessage>(
                (context, _) => ValueTask.FromResult(predicate(context))),
            MiddlewareLifetime.Singleton);
    }

    /// <summary>
    /// Registers an asynchronous predicate filter. When the predicate returns <c>false</c>,
    /// the pipeline is short-circuited and the consumer handler is not invoked.
    /// </summary>
    /// <typeparam name="TMessage">The message type, inferred from the builder.</typeparam>
    /// <param name="builder">The inbound builder to register the filter on.</param>
    /// <param name="predicate">
    /// An asynchronous predicate that receives the consume context and a cancellation token.
    /// Return <c>true</c> to continue the pipeline, <c>false</c> to skip.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    public static IInboundConfigurable<TMessage> Filter<TMessage>(
        this IInboundConfigurable<TMessage> builder,
        Func<ConsumeContext<TMessage>, CancellationToken, ValueTask<bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(predicate);

        return builder.Use(
            _ => new ConsumerFilterMiddleware<TMessage>(predicate),
            MiddlewareLifetime.Singleton);
    }
}

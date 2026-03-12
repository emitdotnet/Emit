namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Defines a filter that determines whether an inbound message should continue through the
/// consumer pipeline. Implement this interface to create reusable, DI-injectable filter logic
/// that can be registered via <c>consumer.Filter&lt;TFilter&gt;()</c>.
/// </summary>
/// <typeparam name="TMessage">The message type to filter.</typeparam>
public interface IConsumerFilter<TMessage>
{
    /// <summary>
    /// Evaluates whether the message should be consumed.
    /// </summary>
    /// <param name="context">The consume context containing the message, features, and scoped services.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><c>true</c> to continue the pipeline and invoke the consumer; <c>false</c> to skip.</returns>
    ValueTask<bool> ShouldConsumeAsync(ConsumeContext<TMessage> context, CancellationToken cancellationToken);
}

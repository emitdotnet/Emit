namespace Emit.Abstractions;

/// <summary>
/// Defines a handler that processes a batch of messages of type <typeparamref name="TValue"/>.
/// Register via <c>group.AddBatchConsumer&lt;T&gt;()</c> on a consumer group builder.
/// The pipeline passes <see cref="ConsumeContext{T}"/> where T is <see cref="MessageBatch{TValue}"/>,
/// so the handler has full access to retry state, transaction context, and transport metadata
/// without any property forwarding or separate context type.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
public interface IBatchConsumer<TValue>
{
    /// <summary>
    /// Processes a batch of messages. Access items via <c>context.Message.Items</c>.
    /// </summary>
    Task ConsumeAsync(ConsumeContext<MessageBatch<TValue>> context, CancellationToken cancellationToken);
}

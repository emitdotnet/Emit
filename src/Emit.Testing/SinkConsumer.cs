namespace Emit.Testing;

using Emit.Abstractions;

/// <summary>
/// Consumer handler that forwards all consumed messages to a <see cref="MessageSink{T}"/>.
/// Register the sink as a singleton in the service collection and use
/// <c>AddConsumer&lt;SinkConsumer&lt;T&gt;&gt;()</c> on the consumer group builder.
/// </summary>
/// <typeparam name="T">The message value type.</typeparam>
public sealed class SinkConsumer<T>(MessageSink<T> sink) : IConsumer<T>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<T> context, CancellationToken cancellationToken)
    {
        return sink.WriteAsync(context, cancellationToken);
    }
}

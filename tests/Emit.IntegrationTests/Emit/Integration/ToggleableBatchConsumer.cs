namespace Emit.IntegrationTests.Integration;

using Emit.Abstractions;
using Emit.Testing;

/// <summary>
/// Batch consumer that either throws or forwards the batch to <see cref="BatchSinkConsumer{T}"/>
/// based on <see cref="ConsumerToggle.ShouldThrow"/>. Used in circuit breaker
/// and error handling tests where failure needs to be toggled at runtime.
/// </summary>
public sealed class ToggleableBatchConsumer(BatchSinkConsumer<string> sink, ConsumerToggle toggle)
    : IBatchConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
    {
        if (toggle.ShouldThrow)
        {
            throw new InvalidOperationException("Simulated batch failure for circuit breaker test.");
        }

        return sink.ConsumeAsync(context, cancellationToken);
    }
}

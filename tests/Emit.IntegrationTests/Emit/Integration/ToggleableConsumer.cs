namespace Emit.IntegrationTests.Integration;

using Emit.Abstractions;
using Emit.Testing;

/// <summary>
/// Consumer that either throws or writes to a <see cref="MessageSink{T}"/>
/// based on <see cref="ConsumerToggle.ShouldThrow"/>. Used in circuit breaker
/// and error handling tests where failure needs to be toggled at runtime.
/// </summary>
public sealed class ToggleableConsumer(MessageSink<string> sink, ConsumerToggle toggle)
    : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        if (toggle.ShouldThrow)
        {
            throw new InvalidOperationException("Simulated failure for circuit breaker test.");
        }

        return sink.WriteAsync(context, cancellationToken);
    }
}

namespace Emit.IntegrationTests.Integration;

using Emit.Abstractions;

/// <summary>
/// Consumer that always throws <see cref="InvalidOperationException"/>, triggering
/// the configured error policy (dead-letter or discard) on every invocation.
/// </summary>
public sealed class AlwaysFailingConsumer : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Simulated persistent consumer failure.");
}

/// <summary>
/// Batch consumer that always throws <see cref="InvalidOperationException"/>, used for
/// testing DLQ routing after retry exhaustion in batch mode.
/// </summary>
public sealed class AlwaysFailingBatchConsumer : IBatchConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Simulated persistent batch consumer failure.");
}

namespace Emit.IntegrationTests.Integration;

using Emit.Abstractions;
using Emit.Testing;

/// <summary>
/// Consumer that forwards dead-lettered messages to a <see cref="MessageSink{T}"/>.
/// Register on DLQ topics to capture messages that were dead-lettered by error policies.
/// The sink must be registered as a singleton so DI resolves it correctly.
/// </summary>
public sealed class DlqCaptureConsumer(MessageSink<byte[]> sink) : IConsumer<byte[]>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<byte[]> context, CancellationToken cancellationToken)
        => sink.WriteAsync(context, cancellationToken);
}

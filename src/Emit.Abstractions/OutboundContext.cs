namespace Emit.Abstractions;

/// <summary>
/// Base context for all outbound (producer) pipelines.
/// Each transport provides an internal subclass (e.g., <c>OutboundKafkaContext</c>)
/// that carries transport-specific metadata.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public abstract class OutboundContext<T> : MessageContext<T>;

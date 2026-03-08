namespace Emit.Abstractions;

/// <summary>
/// Context for all outbound (producer) pipelines.
/// Transport-specific metadata is carried via features on <see cref="MessageContext.Features"/>.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public class OutboundContext<T> : MessageContext<T>;

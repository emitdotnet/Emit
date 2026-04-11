namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Implemented by any builder that owns an outbound middleware pipeline, regardless of whether
/// the message type is known. This non-generic interface enables extension methods that work
/// uniformly across all pipeline levels — global (<c>EmitBuilder</c>), provider (<c>KafkaBuilder</c>),
/// and leaf (<c>KafkaProducerBuilder</c>).
/// </summary>
public interface IOutboundConfigurable
{
    /// <summary>
    /// Gets the outbound middleware pipeline builder.
    /// </summary>
    IMessagePipelineBuilder OutboundPipeline { get; }
}

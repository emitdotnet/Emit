namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Implemented by any builder that owns an inbound middleware pipeline, regardless of whether
/// the message type is known. This non-generic interface enables extension methods that work
/// uniformly across all pipeline levels — global (<c>EmitBuilder</c>), provider (<c>KafkaBuilder</c>,
/// <c>MediatorBuilder</c>), and leaf (<c>KafkaConsumerGroupBuilder</c>, <c>MediatorHandlerBuilder</c>).
/// </summary>
public interface IInboundPipelineConfigurable
{
    /// <summary>
    /// Gets the inbound middleware pipeline builder.
    /// </summary>
    IMessagePipelineBuilder InboundPipeline { get; }
}

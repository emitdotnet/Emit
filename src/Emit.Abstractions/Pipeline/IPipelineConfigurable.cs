namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Implemented by builder classes that expose a middleware pipeline.
/// Enables the <c>Use</c> extension methods for type-based middleware registration.
/// </summary>
public interface IPipelineConfigurable
{
    /// <summary>
    /// Gets the middleware pipeline builder for this configuration scope.
    /// </summary>
    IMessagePipelineBuilder Pipeline { get; }
}

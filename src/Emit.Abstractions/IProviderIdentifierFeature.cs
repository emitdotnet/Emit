namespace Emit.Abstractions;

/// <summary>
/// Feature that identifies which provider owns the current pipeline execution.
/// Set by each provider on the message context before invoking the pipeline.
/// </summary>
public interface IProviderIdentifierFeature
{
    /// <summary>
    /// The provider identifier (e.g., <c>"kafka"</c>).
    /// </summary>
    string ProviderId { get; }
}

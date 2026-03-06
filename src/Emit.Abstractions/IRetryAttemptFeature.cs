namespace Emit.Abstractions;

/// <summary>
/// Provides the current attempt number for a message that is being processed
/// by error handling middleware. Set on the context's feature collection after each
/// failed attempt, making it available to observers and downstream middleware.
/// </summary>
public interface IRetryAttemptFeature
{
    /// <summary>
    /// The current attempt number. <c>0</c> for the initial attempt,
    /// <c>1</c> for the first retry, <c>2</c> for the second retry, and so on.
    /// </summary>
    int Attempt { get; }
}

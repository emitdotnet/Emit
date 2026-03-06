namespace Emit.Abstractions;

/// <summary>
/// Provides the ability to respond to a message.
/// Present for patterns that support request-response (Mediator, Bus).
/// Absent for fire-and-forget patterns (Kafka).
/// </summary>
public interface IResponseFeature
{
    /// <summary>
    /// Gets a value indicating whether a response has already been set.
    /// </summary>
    bool HasResponded { get; }

    /// <summary>
    /// Sets the response value for this message processing operation.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="response">The response value.</param>
    /// <returns>A task that completes when the response has been recorded.</returns>
    /// <exception cref="InvalidOperationException">A response has already been set.</exception>
    Task RespondAsync<TResponse>(TResponse response);
}

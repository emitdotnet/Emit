namespace Emit.Abstractions;

/// <summary>
/// Validates incoming messages before they are processed by a consumer.
/// Return <see cref="MessageValidationResult.Fail(string)"/> for deterministic validation failures.
/// Throw an exception for transient infrastructure errors that should be retried.
/// </summary>
/// <typeparam name="TValue">The message type to validate.</typeparam>
public interface IMessageValidator<in TValue>
{
    /// <summary>
    /// Validates the given message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="MessageValidationResult.Success"/> if the message is valid;
    /// otherwise a failed result created via <see cref="MessageValidationResult.Fail(string)"/>.
    /// </returns>
    Task<MessageValidationResult> ValidateAsync(TValue message, CancellationToken cancellationToken);
}

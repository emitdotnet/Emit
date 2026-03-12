namespace Emit.Abstractions;

/// <summary>
/// Represents an error that occurs when a message fails validation.
/// This type is referenced in dead-letter diagnostic headers to identify validation failures.
/// </summary>
public sealed class MessageValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageValidationException"/> class.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    public MessageValidationException(IReadOnlyList<string> errors)
        : base(string.Join("; ", errors))
    {
        Errors = errors;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageValidationException"/> class.
    /// </summary>
    /// <param name="error">The validation error message.</param>
    public MessageValidationException(string error)
        : base(error)
    {
        Errors = [error];
    }

    /// <summary>
    /// Gets the validation errors that caused this exception.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
}

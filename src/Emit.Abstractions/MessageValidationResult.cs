namespace Emit.Abstractions;

/// <summary>
/// Represents the outcome of message validation performed by an <see cref="IMessageValidator{TValue}"/>.
/// Use <see cref="Success"/> for valid messages and <see cref="Fail(string)"/> or
/// <see cref="Fail(IEnumerable{string})"/> for invalid messages.
/// </summary>
public sealed class MessageValidationResult
{
    private static readonly MessageValidationResult SuccessInstance = new(true, []);

    /// <summary>
    /// A singleton result indicating the message is valid.
    /// </summary>
    public static MessageValidationResult Success => SuccessInstance;

    /// <summary>
    /// Gets a value indicating whether the message passed validation.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the validation errors. Empty when <see cref="IsValid"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    private MessageValidationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary>
    /// Creates a failed validation result with a single error.
    /// </summary>
    /// <param name="error">The validation error message.</param>
    /// <returns>A failed validation result.</returns>
    /// <exception cref="ArgumentException"><paramref name="error"/> is null or whitespace.</exception>
    public static MessageValidationResult Fail(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new MessageValidationResult(false, [error]);
    }

    /// <summary>
    /// Creates a failed validation result with multiple errors.
    /// </summary>
    /// <param name="errors">The validation error messages. Must contain at least one error.</param>
    /// <returns>A failed validation result.</returns>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty.</exception>
    public static MessageValidationResult Fail(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var errorList = errors.ToList();

        if (errorList.Count == 0)
        {
            throw new ArgumentException("At least one error is required.", nameof(errors));
        }

        return new MessageValidationResult(false, errorList);
    }
}

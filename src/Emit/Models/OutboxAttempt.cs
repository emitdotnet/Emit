namespace Emit.Models;

using MessagePack;

/// <summary>
/// Represents a failed processing attempt for an outbox entry.
/// </summary>
/// <param name="AttemptedAt">When this attempt occurred (UTC).</param>
/// <param name="Reason">Categorized reason for failure (e.g., "BrokerUnreachable", "SerializationError").</param>
/// <param name="Message">Human-readable error message.</param>
/// <param name="ExceptionType">Full type name of the exception that caused the failure.</param>
[MessagePackObject]
public sealed record OutboxAttempt(
    [property: Key(0)] DateTime AttemptedAt,
    [property: Key(1)] string Reason,
    [property: Key(2)] string Message,
    [property: Key(3)] string ExceptionType)
{
    /// <summary>
    /// Creates a new <see cref="OutboxAttempt"/> from an exception.
    /// </summary>
    /// <param name="reason">Categorized reason for the failure.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A new outbox attempt record.</returns>
    public static OutboxAttempt FromException(string reason, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new OutboxAttempt(
            AttemptedAt: DateTime.UtcNow,
            Reason: reason,
            Message: exception.Message,
            ExceptionType: exception.GetType().FullName ?? exception.GetType().Name);
    }
}

namespace Emit.Abstractions.ErrorHandling;

/// <summary>
/// Defines the action to take when an error occurs during message processing.
/// Use the static factory methods to create an action.
/// </summary>
public abstract class ErrorAction
{
    private ErrorAction()
    {
    }

    /// <summary>
    /// Retry processing the message up to the specified number of attempts with the given backoff strategy.
    /// When all retries are exhausted, the <paramref name="exhaustionAction"/> is executed.
    /// </summary>
    /// <param name="maxAttempts">The maximum number of retry attempts. Must be greater than zero.</param>
    /// <param name="backoff">The backoff strategy to apply between retries.</param>
    /// <param name="exhaustionAction">The action to take when all retry attempts are exhausted.</param>
    /// <returns>A retry error action.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is less than or equal to zero.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="backoff"/> is null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="exhaustionAction"/> is null.</exception>
    public static ErrorAction Retry(int maxAttempts, Backoff backoff, ErrorAction exhaustionAction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAttempts, 0, nameof(maxAttempts));
        ArgumentNullException.ThrowIfNull(backoff);
        ArgumentNullException.ThrowIfNull(exhaustionAction);
        return new RetryAction(maxAttempts, backoff, exhaustionAction);
    }

    /// <summary>
    /// Send the message to a dead letter destination. The actual destination is resolved from
    /// the dead letter configuration.
    /// </summary>
    /// <returns>A dead letter error action.</returns>
    public static ErrorAction DeadLetter() => new DeadLetterAction(null);

    /// <summary>
    /// Send the message to a specific dead letter topic, overriding the default dead letter configuration.
    /// </summary>
    /// <param name="topicName">The explicit dead letter topic name.</param>
    /// <returns>A dead letter error action targeting the specified topic.</returns>
    /// <exception cref="ArgumentException"><paramref name="topicName"/> is null or whitespace.</exception>
    public static ErrorAction DeadLetter(string topicName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        return new DeadLetterAction(topicName);
    }

    /// <summary>
    /// Discard the message permanently. The message will not be retried or dead-lettered.
    /// </summary>
    /// <returns>A discard error action.</returns>
    public static ErrorAction Discard() => new DiscardAction();

    /// <summary>
    /// Retry action with attempt count, backoff strategy, and exhaustion action.
    /// </summary>
    public sealed class RetryAction(int maxAttempts, Backoff backoff, ErrorAction exhaustionAction) : ErrorAction
    {
        /// <summary>The maximum number of retry attempts.</summary>
        public int MaxAttempts => maxAttempts;

        /// <summary>The backoff strategy between retries.</summary>
        public Backoff Backoff => backoff;

        /// <summary>The action to take when all retry attempts are exhausted.</summary>
        public ErrorAction ExhaustionAction => exhaustionAction;
    }

    /// <summary>
    /// Dead letter action with optional explicit topic name.
    /// </summary>
    public sealed class DeadLetterAction(string? topicName) : ErrorAction
    {
        /// <summary>The explicit dead letter topic name, or <c>null</c> to use the default.</summary>
        public string? TopicName => topicName;
    }

    /// <summary>
    /// Discard the message permanently.
    /// </summary>
    public sealed class DiscardAction : ErrorAction;
}

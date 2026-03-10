namespace Emit.Abstractions;

using Emit.Abstractions.ErrorHandling;

/// <summary>
/// Configures the terminal action for a consumed message. Exactly one action must be chosen.
/// </summary>
public class ErrorActionBuilder
{
    private ErrorAction? terminalAction;
    private int? retryMaxAttempts;
    private Backoff? retryBackoff;

    /// <summary>
    /// Routes the message to the configured dead letter topic.
    /// </summary>
    /// <exception cref="InvalidOperationException">An action has already been configured.</exception>
    public void DeadLetter()
    {
        EnsureTerminalNotSet();
        terminalAction = ErrorAction.DeadLetter();
    }

    /// <summary>
    /// Discards the message permanently. The failure is logged and the message offset
    /// is committed so the consumer moves past the problematic message.
    /// </summary>
    /// <exception cref="InvalidOperationException">An action has already been configured.</exception>
    public void Discard()
    {
        EnsureTerminalNotSet();
        terminalAction = ErrorAction.Discard();
    }

    /// <summary>
    /// Builds the configured action. Throws if no action was configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">No action was configured.</exception>
    public ErrorAction Build()
    {
        if (terminalAction is null)
        {
            if (retryMaxAttempts.HasValue)
            {
                throw new InvalidOperationException(
                    $"Retry must be followed by an exhaustion action. Chain .{nameof(DeadLetter)}() or .{nameof(Discard)}() after .Retry().");
            }

            throw new InvalidOperationException(
                $"An error action is required. Call .{nameof(DeadLetter)}() or .{nameof(Discard)}() on the builder.");
        }

        if (retryMaxAttempts.HasValue)
        {
            return ErrorAction.Retry(retryMaxAttempts.Value, retryBackoff!, terminalAction);
        }

        return terminalAction;
    }

    internal void SetRetry(int maxAttempts, Backoff backoff)
    {
        retryMaxAttempts = maxAttempts;
        retryBackoff = backoff;
    }

    internal void SetTerminalAction(ErrorAction action)
    {
        terminalAction = action;
    }

    /// <summary>
    /// Ensures no terminal action has been configured yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">An action has already been configured.</exception>
    protected void EnsureTerminalNotSet()
    {
        if (terminalAction is not null)
        {
            throw new InvalidOperationException(
                "An action has already been configured. Only one terminal action can be set.");
        }
    }
}

/// <summary>
/// Describes the validator and terminal action configured for a consumer.
/// </summary>
public sealed record ConsumerValidation(
    Type? ValidatorType,
    Delegate? ValidatorDelegate,
    ErrorAction Action);

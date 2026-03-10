namespace Emit.Kafka.Consumer;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Microsoft.Extensions.Logging;

/// <summary>
/// Emits comprehensive structured diagnostics at consumer group startup.
/// </summary>
internal static class StartupDiagnosticsLogger
{
    private static readonly TimeSpan DefaultMaxPollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Logs full startup diagnostics for a consumer group registration.
    /// Emits group-level configuration at Information level and per-consumer
    /// configuration with inline warnings.
    /// </summary>
    internal static void Log<TKey, TValue>(
        ConsumerGroupRegistration<TKey, TValue> registration,
        IDeadLetterSink? deadLetterSink,
        ILogger logger)
    {
        LogGroupConfiguration(registration, logger);

        foreach (var consumerType in registration.ConsumerTypes)
        {
            LogConsumerConfiguration(registration, deadLetterSink, consumerType, logger);
        }
    }

    private static void LogGroupConfiguration<TKey, TValue>(
        ConsumerGroupRegistration<TKey, TValue> registration,
        ILogger logger)
    {
        logger.LogInformation(
            "Consumer group {GroupId} on topic {Topic}: " +
            "Workers={WorkerCount}, Distribution={WorkerDistribution}, " +
            "Buffer={BufferSize}, CommitInterval={CommitInterval}, StopTimeout={StopTimeout}, " +
            "CircuitBreaker={CircuitBreaker}, RateLimit={RateLimit}, " +
            "DeserializationError={DeserializationError}",
            registration.GroupId, registration.TopicName,
            registration.WorkerCount, registration.WorkerDistribution,
            registration.BufferSize, registration.CommitInterval, registration.WorkerStopTimeout,
            registration.CircuitBreakerEnabled ? "Enabled" : "Disabled",
            registration.RateLimitEnabled ? "Enabled" : "Disabled",
            FormatAction(registration.DeserializationErrorAction));

        if (registration.DeserializationErrorAction is null)
        {
            logger.LogWarning(
                "No OnDeserializationError configured for consumer group {GroupId}. " +
                "Deserialization errors will be discarded.",
                registration.GroupId);
        }
    }

    private static void LogConsumerConfiguration<TKey, TValue>(
        ConsumerGroupRegistration<TKey, TValue> registration,
        IDeadLetterSink? deadLetterSink,
        Type consumerType,
        ILogger logger)
    {
        var policy = registration.GroupErrorPolicy;
        var policySource = policy is null ? "None" : "Group";

        var dlqDestination = deadLetterSink is not null
            ? EmitEndpointAddress.GetEntityName(deadLetterSink.DestinationAddress) ?? deadLetterSink.DestinationAddress.ToString()
            : "None";
        var worstCaseRetry = policy is not null ? ComputeWorstCaseRetryDuration(policy) : TimeSpan.Zero;

        logger.LogInformation(
            "  [{GroupId}] {Handler}: " +
            "ErrorPolicy={PolicySource}, ErrorHandling={ErrorHandling}, " +
            "DLQ={DlqDestination}, WorstCaseRetry={WorstCaseRetry}",
            registration.GroupId, consumerType.Name,
            policySource, FormatErrorPolicy(policy),
            dlqDestination, worstCaseRetry);

        // Warning: no error handling configured
        if (policy is null)
        {
            logger.LogWarning(
                "No error handling configured for consumer {Handler} in group {GroupId}. " +
                "Failed messages will be discarded without retry.",
                consumerType.Name, registration.GroupId);
        }

        // Warning: worst-case retry exceeds max.poll.interval.ms
        if (worstCaseRetry > DefaultMaxPollInterval)
        {
            logger.LogWarning(
                "Consumer {Handler} in group {GroupId}: worst-case retry duration {WorstCaseRetry} " +
                "exceeds max.poll.interval.ms ({MaxPollInterval}). Consumer may be kicked from group.",
                consumerType.Name, registration.GroupId, worstCaseRetry, DefaultMaxPollInterval);
        }

        // Warning: retries without circuit breaker
        if (HasRetries(policy) && !registration.CircuitBreakerEnabled)
        {
            logger.LogWarning(
                "Consumer {Handler} in group {GroupId} configured with retries but no circuit breaker.",
                consumerType.Name, registration.GroupId);
        }

        // Warning: DLQ loop risk — consumer's source topic equals the DLQ destination
        var dlqEntityName = deadLetterSink is not null
            ? EmitEndpointAddress.GetEntityName(deadLetterSink.DestinationAddress)
            : null;
        if (dlqEntityName is not null && dlqEntityName.Equals(registration.TopicName, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Consumer {Handler} on topic {Topic} in group {GroupId} may create infinite loop — " +
                "consuming from DLQ topic with dead-lettering configured.",
                consumerType.Name, registration.TopicName, registration.GroupId);
        }
    }

    private static string FormatErrorPolicy(ErrorPolicy? policy)
    {
        if (policy is null)
        {
            return "None (discard)";
        }

        var parts = new List<string>();

        foreach (var clause in policy.Clauses)
        {
            var actionStr = clause.Action is not null ? FormatAction(clause.Action) : "Default";
            parts.Add($"When<{clause.ExceptionType.Name}>: {actionStr}");
        }

        parts.Add($"Default: {FormatAction(policy.DefaultAction)}");

        return string.Join(", ", parts);
    }

    private static string FormatAction(ErrorAction? action) => action switch
    {
        null => "None",
        ErrorAction.RetryAction r => $"Retry({r.MaxAttempts}x, then {FormatAction(r.ExhaustionAction)})",
        ErrorAction.DeadLetterAction => "DeadLetter",
        ErrorAction.DiscardAction => "Discard",
        _ => action.GetType().Name,
    };

    private static TimeSpan ComputeWorstCaseRetryDuration(ErrorPolicy policy)
    {
        var maxDuration = ComputeRetryDuration(policy.DefaultAction);

        foreach (var clause in policy.Clauses)
        {
            if (clause.Action is not null)
            {
                var duration = ComputeRetryDuration(clause.Action);
                if (duration > maxDuration)
                {
                    maxDuration = duration;
                }
            }
        }

        return maxDuration;
    }

    private static TimeSpan ComputeRetryDuration(ErrorAction action)
    {
        if (action is not ErrorAction.RetryAction retry)
        {
            return TimeSpan.Zero;
        }

        return retry.Backoff.ComputeTotalDelay(retry.MaxAttempts);
    }

    private static bool HasRetries(ErrorPolicy? policy)
    {
        if (policy is null)
        {
            return false;
        }

        if (policy.DefaultAction is ErrorAction.RetryAction)
        {
            return true;
        }

        foreach (var clause in policy.Clauses)
        {
            if (clause.Action is ErrorAction.RetryAction)
            {
                return true;
            }
        }

        return false;
    }
}

namespace Emit.Tracing;

/// <summary>
/// Well-known Activity tag names used across Emit tracing middleware.
/// </summary>
internal static class ActivityTagNames
{
    // OpenTelemetry semantic conventions for messaging
    public const string MessagingSystem = "messaging.system";
    public const string MessagingOperation = "messaging.operation";
    public const string MessagingDestinationName = "messaging.destination.name";
    public const string MessagingRetryAttempt = "messaging.retry.attempt";
    public const string MessagingRetryMaxAttempts = "messaging.retry.max_attempts";

    // Emit-specific tags
    public const string NodeId = "emit.node.id";
    public const string MessageType = "emit.message.type";
    public const string KeyType = "emit.key.type";
    public const string Sequence = "emit.sequence";
    public const string GroupKey = "emit.group.key";
    public const string Consumer = "emit.consumer";
    public const string ConsumerType = "emit.consumer.type";
    public const string RouteKey = "emit.route.key";

    // DLQ-specific tags
    public const string DlqReplay = "emit.dlq.replay";
    public const string DlqOriginalTopic = "emit.dlq.original_topic";
    public const string DlqReason = "emit.dlq.reason";
}

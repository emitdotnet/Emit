namespace Emit.Tracing;

/// <summary>
/// Well-known Activity tag names: the OpenTelemetry semantic conventions Emit sets, and Emit's own tags.
/// </summary>
internal static class ActivityTagNames
{
    // OpenTelemetry semantic conventions for messaging
    public const string MessagingSystem = "messaging.system";
    public const string MessagingOperationName = "messaging.operation.name";
    public const string MessagingOperationType = "messaging.operation.type";
    public const string MessagingDestinationName = "messaging.destination.name";
    public const string MessagingMessageId = "messaging.message.id";
    public const string MessagingBatchMessageCount = "messaging.batch.message_count";

    // OpenTelemetry general semantic conventions
    public const string ServerAddress = "server.address";
    public const string ServerPort = "server.port";
    public const string ErrorType = "error.type";

    // Emit-specific tags
    public const string NodeId = "emit.node.id";
    public const string MessageType = "emit.message.type";
    public const string Sequence = "emit.sequence";
    public const string GroupKey = "emit.group.key";
    public const string Consumer = "emit.consumer";
    public const string ConsumerType = "emit.consumer.type";
    public const string RouteKey = "emit.route.key";
    public const string RetryAttempt = "emit.retry.attempt";
    public const string RetryMaxAttempts = "emit.retry.max_attempts";
}

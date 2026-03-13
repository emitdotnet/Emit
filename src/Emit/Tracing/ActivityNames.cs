namespace Emit.Tracing;

/// <summary>
/// Well-known Activity names used across Emit tracing middleware.
/// </summary>
internal static class ActivityNames
{
    public const string Receive = "emit.receive";
    public const string Produce = "emit.produce";
    public const string Consume = "emit.consume";
    public const string ConsumeRetry = "emit.consume.retry";
    public const string DlqReplay = "emit.dlq.replay";
    public const string DlqPublish = "emit.dlq.publish";
    public const string OutboxProcess = "emit.outbox.process";
}

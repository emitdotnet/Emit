namespace Emit.Tracing;

/// <summary>
/// Operation names of Emit's spans. A messaging span is named after its operation and destination, following the
/// OpenTelemetry messaging conventions: <c>{operation} {destination}</c>.
/// </summary>
internal static class ActivityNames
{
    /// <summary>A message is created: its context becomes the message's creation context.</summary>
    public const string Create = "create";

    /// <summary>A message is sent to the message broker.</summary>
    public const string Send = "send";

    /// <summary>A consumer processes a message, or a batch of messages.</summary>
    public const string Process = "process";

    /// <summary>A retry attempt of processing a message, within its process span.</summary>
    public const string Retry = "emit.retry";
}

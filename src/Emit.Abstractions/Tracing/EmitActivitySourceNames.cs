namespace Emit.Abstractions.Tracing;

/// <summary>
/// Provides constant names for Emit ActivitySources.
/// </summary>
/// <remarks>
/// Use these constants when configuring OpenTelemetry to listen to Emit traces.
/// </remarks>
public static class EmitActivitySourceNames
{
    /// <summary>
    /// ActivitySource name for producing messages: creating a message, and sending it to the message broker,
    /// directly or from the outbox.
    /// </summary>
    /// <remarks>
    /// Value: "Emit.Producer"
    /// </remarks>
    public const string Producer = "Emit.Producer";

    /// <summary>
    /// ActivitySource name for consuming messages: processing a message, its retries, and sending it to a dead
    /// letter topic.
    /// </summary>
    /// <remarks>
    /// Value: "Emit.Consumer"
    /// </remarks>
    public const string Consumer = "Emit.Consumer";

    /// <summary>
    /// ActivitySource name for mediator requests.
    /// </summary>
    /// <remarks>
    /// Value: "Emit.Mediator"
    /// </remarks>
    public const string Mediator = "Emit.Mediator";
}

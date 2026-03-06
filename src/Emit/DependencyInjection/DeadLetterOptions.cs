namespace Emit.DependencyInjection;

/// <summary>
/// Configures global dead letter queue (DLQ) behavior.
/// </summary>
public sealed class DeadLetterOptions
{
    /// <summary>
    /// A function that derives the dead letter topic name from the source topic name.
    /// Applied when a consumer uses dead lettering without specifying an explicit topic name.
    /// </summary>
    /// <remarks>
    /// The default convention appends <c>.dlt</c> to the source topic name
    /// (e.g., <c>"orders"</c> becomes <c>"orders.dlt"</c>).
    /// </remarks>
    public Func<string, string> TopicNamingConvention { get; set; } = sourceTopic => $"{sourceTopic}.dlt";
}

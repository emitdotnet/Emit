namespace Emit.Abstractions;

/// <summary>
/// Well-known message header name constants for trace context and address propagation.
/// </summary>
public static class WellKnownHeaders
{
    /// <summary>W3C Trace Context traceparent header.</summary>
    public const string TraceParent = "traceparent";

    /// <summary>W3C Trace Context tracestate header.</summary>
    public const string TraceState = "tracestate";

    /// <summary>W3C Baggage header.</summary>
    public const string Baggage = "baggage";

    /// <summary>Source address header. Injected by the producer so consumers can identify the sender.</summary>
    public const string SourceAddress = "emit-source-address";

    /// <summary>
    /// Message id header. Injected by the producer, so every delivery of the message has the same
    /// <see cref="MessageContext.MessageId"/>.
    /// </summary>
    public const string MessageId = "emit-message-id";
}

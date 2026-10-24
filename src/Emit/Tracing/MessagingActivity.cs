namespace Emit.Tracing;

using System.Diagnostics;
using System.Net;
using System.Text;
using Emit.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Starts Emit's spans and carries their context in message headers, following the OpenTelemetry messaging
/// conventions. Every span Emit starts goes through <see cref="StartActivity"/>, so the tracing options apply the
/// same way to all of them.
/// </summary>
internal static class MessagingActivity
{
    /// <summary>
    /// Returns whether a span of <paramref name="source"/> may start: tracing is enabled and something listens to
    /// the source. Checked before preparing a span's attributes and links, so nothing is spent when it may not.
    /// </summary>
    /// <param name="source">The source of the span.</param>
    /// <param name="options">The tracing options.</param>
    public static bool IsEnabled(ActivitySource source, EmitTracingOptions options) =>
        options.Enabled && source.HasListeners();

    /// <summary>
    /// Starts a messaging span named <c>{operation} {destination}</c>, with its attributes given at creation: the
    /// messaging system, the operation, the destination and the server, which samplers can use, the message id, and
    /// <paramref name="tags"/>.
    /// </summary>
    /// <param name="source">The source of the span.</param>
    /// <param name="options">The tracing options.</param>
    /// <param name="operation">The operation, which is both its name and its type: create, send or process.</param>
    /// <param name="kind">The span kind of the operation.</param>
    /// <param name="destination">The address of the destination the operation targets.</param>
    /// <param name="messageId">The id of the message, or <see langword="null"/> for a span of a batch.</param>
    /// <param name="parent">The parent context, or the default to use the current activity.</param>
    /// <param name="links">The contexts the span links to.</param>
    /// <param name="tags">More attributes to give at creation.</param>
    /// <returns>The started span, or <see langword="null"/> when none is started.</returns>
    public static Activity? Start(
        ActivitySource source,
        EmitTracingOptions options,
        string operation,
        ActivityKind kind,
        Uri? destination,
        string? messageId,
        ActivityContext parent = default,
        IEnumerable<ActivityLink>? links = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        var destinationName = EmitEndpointAddress.GetEntityName(destination);
        List<KeyValuePair<string, object?>> creationTags =
        [
            new(ActivityTagNames.MessagingSystem, EmitEndpointAddress.GetScheme(destination)),
            new(ActivityTagNames.MessagingOperationName, operation),
            new(ActivityTagNames.MessagingOperationType, operation),
        ];

        if (destinationName is not null)
        {
            creationTags.Add(new(ActivityTagNames.MessagingDestinationName, destinationName));
        }

        if (destination is { Host.Length: > 0 })
        {
            creationTags.Add(new(ActivityTagNames.ServerAddress, destination.Host));
            if (destination.Port > 0)
            {
                creationTags.Add(new(ActivityTagNames.ServerPort, destination.Port));
            }
        }

        if (messageId is not null)
        {
            creationTags.Add(new(ActivityTagNames.MessagingMessageId, messageId));
        }

        if (tags is not null)
        {
            creationTags.AddRange(tags);
        }

        var name = destinationName is null ? operation : $"{operation} {destinationName}";
        return StartActivity(source, options, name, kind, parent, links, creationTags);
    }

    /// <summary>
    /// Starts a span when tracing is enabled, and when the span joins a trace through its parent, the current
    /// activity or a link. A span that would start a new trace is started only when
    /// <see cref="EmitTracingOptions.CreateRootActivities"/> allows it.
    /// </summary>
    /// <param name="source">The source of the span.</param>
    /// <param name="options">The tracing options.</param>
    /// <param name="name">The span name.</param>
    /// <param name="kind">The span kind.</param>
    /// <param name="parent">The parent context, or the default to use the current activity.</param>
    /// <param name="links">The contexts the span links to.</param>
    /// <param name="tags">The attributes to give at creation.</param>
    /// <returns>The started span, or <see langword="null"/> when none is started.</returns>
    public static Activity? StartActivity(
        ActivitySource source,
        EmitTracingOptions options,
        string name,
        ActivityKind kind,
        ActivityContext parent = default,
        IEnumerable<ActivityLink>? links = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        if (!IsEnabled(source, options))
        {
            return null;
        }

        var linkList = links?.ToList();
        var joinsTrace = parent != default || Activity.Current is not null || linkList is { Count: > 0 };
        if (!joinsTrace && !options.CreateRootActivities)
        {
            return null;
        }

        return source.StartActivity(name, kind, parent, tags, linkList);
    }

    /// <summary>
    /// Returns the creation context of a message: the trace context its producer put in its headers, or the
    /// default when it has none.
    /// </summary>
    /// <param name="headers">The headers of the message.</param>
    public static ActivityContext CreationContext(IReadOnlyList<KeyValuePair<string, string>>? headers) =>
        ParseContext(headers.GetFirstValue(WellKnownHeaders.TraceParent), headers.GetFirstValue(WellKnownHeaders.TraceState));

    /// <summary>
    /// Returns the creation context of a message whose header values are UTF-8 bytes, as stored in the outbox.
    /// </summary>
    /// <param name="headers">The headers of the message.</param>
    public static ActivityContext CreationContext(IReadOnlyList<KeyValuePair<string, byte[]>>? headers) =>
        ParseContext(
            headers.GetFirstValue(WellKnownHeaders.TraceParent) is { } traceParent ? Encoding.UTF8.GetString(traceParent) : null,
            headers.GetFirstValue(WellKnownHeaders.TraceState) is { } traceState ? Encoding.UTF8.GetString(traceState) : null);

    /// <summary>
    /// Makes <paramref name="activity"/> the creation context of a message: puts its W3C trace context, and its
    /// baggage when propagated, in the message headers, replacing any already there.
    /// </summary>
    /// <param name="activity">The span that creates the message.</param>
    /// <param name="headers">The headers of the message.</param>
    /// <param name="options">The tracing options.</param>
    /// <param name="logger">The logger warning about baggage over the size limit.</param>
    public static void Inject(
        Activity activity,
        List<KeyValuePair<string, string>> headers,
        EmitTracingOptions options,
        ILogger logger)
    {
        if (activity.Id is { } traceParent)
        {
            headers.Upsert(WellKnownHeaders.TraceParent, traceParent);
        }

        if (activity.TraceStateString is { } traceState)
        {
            headers.Upsert(WellKnownHeaders.TraceState, traceState);
        }

        if (options.PropagateBaggage)
        {
            var baggage = activity.Baggage
                .Where(b => b.Value is not null)
                .Select(b => new KeyValuePair<string, string>(b.Key, b.Value!));

            if (BuildBaggageHeader(baggage, options.MaxBaggageSizeBytes, logger) is { } baggageHeader)
            {
                headers.Upsert(WellKnownHeaders.Baggage, baggageHeader);
            }
        }
    }

    /// <summary>
    /// Restores on <paramref name="activity"/> the W3C baggage the message headers carry, when baggage is
    /// propagated. Members are comma-separated <c>key=value</c> pairs; their properties are ignored.
    /// </summary>
    /// <param name="activity">The span processing the message.</param>
    /// <param name="headers">The headers of the message.</param>
    /// <param name="options">The tracing options.</param>
    public static void RestoreBaggage(
        Activity activity,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        EmitTracingOptions options)
    {
        if (!options.PropagateBaggage || headers.GetFirstValue(WellKnownHeaders.Baggage) is not { Length: > 0 } baggageHeaderValue)
        {
            return;
        }

        foreach (var member in baggageHeaderValue.Split(','))
        {
            var trimmed = member.Trim();
            if (trimmed.Length == 0)
                continue;

            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            // Properties (;property=value) are ignored: only key=value is kept
            var valueEnd = trimmed.IndexOf(';', equalsIndex);
            var key = WebUtility.UrlDecode(trimmed[..equalsIndex].Trim());
            var value = WebUtility.UrlDecode(
                valueEnd > 0
                    ? trimmed[(equalsIndex + 1)..valueEnd].Trim()
                    : trimmed[(equalsIndex + 1)..].Trim());

            activity.AddBaggage(key, value);
        }
    }

    /// <summary>
    /// Records on <paramref name="activity"/> that its operation failed with <paramref name="exception"/>, unless
    /// the exception is the cancellation <paramref name="cancellationToken"/> asked for: a cancellation is not a
    /// failure of the operation.
    /// </summary>
    /// <param name="activity">The span of the operation, or <see langword="null"/> when none was started.</param>
    /// <param name="exception">The exception the operation ended with.</param>
    /// <param name="cancellationToken">The token the operation was given.</param>
    public static void RecordFailure(Activity? activity, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return;
        }

        RecordFailure(activity, exception);
    }

    /// <summary>
    /// Records on <paramref name="activity"/> that its operation failed with <paramref name="exception"/>.
    /// </summary>
    /// <param name="activity">The span of the operation, or <see langword="null"/> when none was started.</param>
    /// <param name="exception">The exception the operation failed with.</param>
    public static void RecordFailure(Activity? activity, Exception exception) =>
        RecordFailure(activity, exception.GetType().FullName ?? exception.GetType().Name, exception.Message);

    /// <summary>
    /// Records on <paramref name="activity"/> that its operation failed: status Error with
    /// <paramref name="description"/>, and <c>error.type</c>.
    /// </summary>
    /// <param name="activity">The span of the operation, or <see langword="null"/> when none was started.</param>
    /// <param name="errorType">The low-cardinality type of the error.</param>
    /// <param name="description">What failed.</param>
    public static void RecordFailure(Activity? activity, string errorType, string? description)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, description);
        activity.SetTag(ActivityTagNames.ErrorType, errorType);
    }

    /// <summary>
    /// Returns the W3C baggage header value of <paramref name="baggage"/>, dropping the members past
    /// <paramref name="maxSizeBytes"/> with a warning, or <see langword="null"/> when there is no baggage.
    /// </summary>
    private static string? BuildBaggageHeader(
        IEnumerable<KeyValuePair<string, string>> baggage,
        int maxSizeBytes,
        ILogger logger)
    {
        var members = new List<string>();
        var totalSize = 0;

        foreach (var (key, value) in baggage)
        {
            var encodedKey = WebUtility.UrlEncode(key);
            var encodedValue = WebUtility.UrlEncode(value);
            var member = $"{encodedKey}={encodedValue}";

            // +2 for ", " separator (except first)
            var additionalSize = member.Length + (members.Count > 0 ? 2 : 0);

            if (totalSize + additionalSize > maxSizeBytes)
            {
                logger.LogWarning(
                    "Baggage size exceeds limit {Limit} bytes. Dropping remaining items.",
                    maxSizeBytes);
                break;
            }

            members.Add(member);
            totalSize += additionalSize;
        }

        return members.Count > 0 ? string.Join(", ", members) : null;
    }

    private static ActivityContext ParseContext(string? traceParent, string? traceState) =>
        ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var context) ? context : default;
}

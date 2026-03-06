namespace Emit.Consumer;

using System.Collections.Frozen;

/// <summary>
/// Immutable map resolving dead letter destinations for source topics.
/// Built eagerly at registration time by walking error policies.
/// </summary>
public sealed class DeadLetterTopicMap
{
    /// <summary>
    /// An empty map that resolves nothing.
    /// </summary>
    public static DeadLetterTopicMap Empty { get; } = new([]);

    private readonly FrozenDictionary<(string? ConsumerKey, string SourceTopic), string> map;

    /// <summary>
    /// All unique dead letter topic names in the map.
    /// </summary>
    public IReadOnlySet<string> AllTopics { get; }

    /// <summary>
    /// Creates a new dead letter topic map from the given entries.
    /// </summary>
    /// <param name="entries">
    /// Entries mapping (consumerKey, sourceTopic) to a dead letter destination.
    /// A <c>null</c> consumerKey indicates a group-level entry (e.g., deserialization errors).
    /// A non-null consumerKey indicates a consumer-level entry (e.g., consumer error handling).
    /// </param>
    public DeadLetterTopicMap(IEnumerable<DeadLetterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var dict = new Dictionary<(string? ConsumerKey, string SourceTopic), string>();
        var topics = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            dict[(entry.ConsumerKey, entry.SourceTopic)] = entry.DeadLetterTopic;
            topics.Add(entry.DeadLetterTopic);
        }

        map = dict.ToFrozenDictionary();
        AllTopics = topics;
    }

    /// <summary>
    /// Resolves the dead letter topic for the given consumer and source topic.
    /// When <paramref name="consumerKey"/> is non-null, first attempts a consumer-level lookup;
    /// if not found, falls back to a group-level lookup (null consumerKey).
    /// </summary>
    /// <param name="consumerKey">
    /// The consumer key for consumer-level resolution, or <c>null</c> for group-level resolution.
    /// </param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <returns>The dead letter topic name, or <c>null</c> if none is configured.</returns>
    public string? Resolve(string? consumerKey, string sourceTopic)
    {
        // Consumer-level lookup
        if (consumerKey is not null && map.TryGetValue((consumerKey, sourceTopic), out var consumerDlq))
        {
            return consumerDlq;
        }

        // Group-level fallback
        if (map.TryGetValue((null, sourceTopic), out var groupDlq))
        {
            return groupDlq;
        }

        return null;
    }
}

/// <summary>
/// A single entry in the dead letter topic map.
/// </summary>
public sealed class DeadLetterEntry
{
    /// <summary>
    /// The consumer key for consumer-level mappings, or <c>null</c> for group-level mappings.
    /// </summary>
    public required string? ConsumerKey { get; init; }

    /// <summary>
    /// The source topic name.
    /// </summary>
    public required string SourceTopic { get; init; }

    /// <summary>
    /// The dead letter topic name.
    /// </summary>
    public required string DeadLetterTopic { get; init; }
}

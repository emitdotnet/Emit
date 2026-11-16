namespace Emit.MongoDB.Configuration;

/// <summary>
/// Configuration options for the MongoDB inbox.
/// </summary>
public sealed class MongoDbInboxOptions
{
    /// <summary>
    /// Gets or sets how long a handled message stays recorded. A copy that arrives later is handled again. MongoDB
    /// removes an expired record typically within a minute. Default: 7 days.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}

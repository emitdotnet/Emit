namespace Emit.Consumer;

using Microsoft.Extensions.Logging;

/// <summary>
/// Verifies that all required dead letter topics exist at startup.
/// </summary>
public static class DlqTopicVerifier
{
    /// <summary>
    /// Verifies that all required DLQ topics exist. Throws if any are missing.
    /// </summary>
    /// <param name="requiredTopics">The set of DLQ topics that must exist.</param>
    /// <param name="getExistingTopics">A function that returns the set of existing topic names. May throw on timeout or connection error.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <exception cref="InvalidOperationException">One or more DLQ topics do not exist.</exception>
    public static void Verify(
        IReadOnlySet<string> requiredTopics,
        Func<IReadOnlySet<string>> getExistingTopics,
        ILogger logger)
    {
        if (requiredTopics.Count == 0)
        {
            return;
        }

        var existingTopics = getExistingTopics();

        foreach (var topic in requiredTopics)
        {
            if (!existingTopics.Contains(topic))
            {
                throw new InvalidOperationException(
                    $"DLQ topic '{topic}' does not exist. Create it before starting the application.");
            }
        }

        logger.LogDebug("Verified {Count} DLQ topic(s) exist", requiredTopics.Count);
    }
}

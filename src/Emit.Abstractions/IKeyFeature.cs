namespace Emit.Abstractions;

/// <summary>
/// Provides access to the message key for patterns that use keyed messages (e.g., Kafka).
/// Set on <see cref="MessageContext.Features"/> when key data is available.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IKeyFeature<out TKey>
{
    /// <summary>
    /// The message key.
    /// </summary>
    TKey Key { get; }
}

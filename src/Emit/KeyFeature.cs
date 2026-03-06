namespace Emit;

using Emit.Abstractions;

/// <summary>
/// Default implementation of <see cref="IKeyFeature{TKey}"/>.
/// </summary>
public sealed class KeyFeature<TKey>(TKey key) : IKeyFeature<TKey>, IKeyTypeFeature
{
    /// <inheritdoc />
    public TKey Key => key;

    /// <inheritdoc />
    public Type KeyType => typeof(TKey);
}

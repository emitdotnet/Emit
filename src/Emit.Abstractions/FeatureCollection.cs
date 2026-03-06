namespace Emit.Abstractions;

/// <summary>
/// Default implementation of <see cref="IFeatureCollection"/> backed by a dictionary.
/// </summary>
internal sealed class FeatureCollection : IFeatureCollection
{
    private readonly Dictionary<Type, object> features = [];

    /// <inheritdoc />
    public TFeature? Get<TFeature>() where TFeature : class
    {
        return features.TryGetValue(typeof(TFeature), out var value) ? (TFeature)value : null;
    }

    /// <inheritdoc />
    public void Set<TFeature>(TFeature feature) where TFeature : class
    {
        ArgumentNullException.ThrowIfNull(feature);
        features[typeof(TFeature)] = feature;
    }
}

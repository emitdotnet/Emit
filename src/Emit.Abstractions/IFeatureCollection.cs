namespace Emit.Abstractions;

/// <summary>
/// A collection of optional, pattern-specific features that can be accessed by middleware.
/// Features provide a type-safe extension mechanism — middleware checks for the presence
/// of a feature and gracefully handles its absence.
/// </summary>
public interface IFeatureCollection
{
    /// <summary>
    /// Gets the feature of the specified type, or <c>null</c> if not present.
    /// </summary>
    /// <typeparam name="TFeature">The feature interface type.</typeparam>
    /// <returns>The feature instance, or <c>null</c> if not set.</returns>
    TFeature? Get<TFeature>() where TFeature : class;

    /// <summary>
    /// Sets or replaces the feature of the specified type.
    /// </summary>
    /// <typeparam name="TFeature">The feature interface type.</typeparam>
    /// <param name="feature">The feature instance.</param>
    void Set<TFeature>(TFeature feature) where TFeature : class;
}

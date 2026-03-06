namespace Emit.Abstractions;

/// <summary>
/// Provides random number generation.
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Returns a random integer in the range [<paramref name="minValue"/>, <paramref name="maxValue"/>).
    /// </summary>
    /// <param name="minValue">Inclusive lower bound.</param>
    /// <param name="maxValue">Exclusive upper bound.</param>
    int Next(int minValue, int maxValue);

    /// <summary>
    /// Returns a random floating-point number in the range [0.0, 1.0).
    /// </summary>
    double NextDouble();
}

namespace Emit.UnitTests.Abstractions;

using global::Emit.Abstractions;

/// <summary>
/// Test implementation of <see cref="IRandomProvider"/> that returns fixed values.
/// </summary>
internal sealed class TestRandomProvider(int fixedInt = 0, double fixedDouble = 0.0) : IRandomProvider
{
    public int Next(int minValue, int maxValue) => fixedInt;

    public double NextDouble() => fixedDouble;
}

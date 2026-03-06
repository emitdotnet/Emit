namespace Emit.Abstractions;

/// <summary>
/// Default <see cref="IRandomProvider"/> implementation using <see cref="Random.Shared"/>.
/// </summary>
internal sealed class DefaultRandomProvider : IRandomProvider
{
    /// <inheritdoc />
    public int Next(int minValue, int maxValue) => Random.Shared.Next(minValue, maxValue);

    /// <inheritdoc />
    public double NextDouble() => Random.Shared.NextDouble();
}

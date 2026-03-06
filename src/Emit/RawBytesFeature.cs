namespace Emit;

using Emit.Abstractions;

/// <summary>
/// Default implementation of <see cref="IRawBytesFeature"/>.
/// </summary>
public sealed class RawBytesFeature(byte[]? rawKey, byte[]? rawValue) : IRawBytesFeature
{
    /// <inheritdoc />
    public byte[]? RawKey => rawKey;

    /// <inheritdoc />
    public byte[]? RawValue => rawValue;
}

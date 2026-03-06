namespace Emit;

using Emit.Abstractions.Pipeline;

/// <summary>
/// Default implementation of <see cref="IHeadersFeature"/>.
/// </summary>
public sealed class HeadersFeature(IReadOnlyList<KeyValuePair<string, string>> headers) : IHeadersFeature
{
    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<string, string>> Headers => headers;
}

namespace Emit.Abstractions;

/// <summary>
/// Extension methods for message header lists. Header keys match without regard to case, and a key may repeat.
/// </summary>
public static class HeaderExtensions
{
    /// <summary>
    /// Returns the value of the first header with the key <paramref name="key"/>, or <see langword="null"/> when
    /// there is none.
    /// </summary>
    /// <typeparam name="TValue">The header value type.</typeparam>
    /// <param name="headers">The headers to search.</param>
    /// <param name="key">The header key.</param>
    public static TValue? GetFirstValue<TValue>(this IEnumerable<KeyValuePair<string, TValue>>? headers, string key)
        where TValue : class =>
        headers?.FirstOrDefault(h => Matches(h.Key, key)).Value;

    /// <summary>
    /// Returns the value of the last header with the key <paramref name="key"/>, or <see langword="null"/> when
    /// there is none.
    /// </summary>
    /// <typeparam name="TValue">The header value type.</typeparam>
    /// <param name="headers">The headers to search.</param>
    /// <param name="key">The header key.</param>
    public static TValue? GetLastValue<TValue>(this IEnumerable<KeyValuePair<string, TValue>>? headers, string key)
        where TValue : class =>
        headers?.LastOrDefault(h => Matches(h.Key, key)).Value;

    /// <summary>
    /// Sets the header <paramref name="key"/> to <paramref name="value"/>: every header with that key is removed
    /// and the header is added once. Use it for headers that may only appear once on a message.
    /// </summary>
    /// <param name="headers">The headers to update.</param>
    /// <param name="key">The header key.</param>
    /// <param name="value">The header value.</param>
    public static void Upsert(this List<KeyValuePair<string, string>> headers, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(headers);
        headers.RemoveAll(h => Matches(h.Key, key));
        headers.Add(new(key, value));
    }

    private static bool Matches(string headerKey, string key) =>
        string.Equals(headerKey, key, StringComparison.OrdinalIgnoreCase);
}

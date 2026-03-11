namespace Emit.Abstractions;

/// <summary>
/// Represents a transport endpoint address as a URI.
/// Format: <c>{scheme}://{host}:{port}/{entityName}</c>.
/// </summary>
public readonly record struct EmitEndpointAddress
{
    /// <summary>
    /// The transport scheme (e.g. "kafka", "rabbitmq").
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// The broker host name.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// The broker port, or <c>null</c> if not specified.
    /// </summary>
    public int? Port { get; }

    /// <summary>
    /// The entity name (topic, queue, exchange, etc.), or <c>null</c> for host-only addresses.
    /// </summary>
    public string? EntityName { get; }

    private EmitEndpointAddress(string scheme, string host, int? port, string? entityName)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
        EntityName = entityName;
    }

    /// <summary>
    /// Creates a host-only address (no entity name). Used for <c>SourceAddress</c>.
    /// </summary>
    public static EmitEndpointAddress ForHost(string scheme, string host, int? port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return new EmitEndpointAddress(scheme, host, port, null);
    }

    /// <summary>
    /// Creates an entity address (host + entity name). Used for <c>DestinationAddress</c>.
    /// </summary>
    public static EmitEndpointAddress ForEntity(string scheme, string host, int? port, string entityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        return new EmitEndpointAddress(scheme, host, port, entityName);
    }

    /// <summary>
    /// Parses an <see cref="EmitEndpointAddress"/> from a <see cref="Uri"/>.
    /// </summary>
    public static EmitEndpointAddress FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var scheme = uri.Scheme;
        var host = uri.Host;
        var port = uri.IsDefaultPort ? (int?)null : uri.Port;

        var path = uri.AbsolutePath.TrimStart('/');
        var entityName = string.IsNullOrEmpty(path) ? null : path;
        return new EmitEndpointAddress(scheme, host, port, entityName);
    }

    /// <summary>
    /// Extracts the entity name (path segment) from a URI.
    /// Returns <c>null</c> if the URI is null or has no path.
    /// </summary>
    public static string? GetEntityName(Uri? uri)
    {
        if (uri is null) return null;

        var path = uri.AbsolutePath.TrimStart('/');
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// Extracts the URI scheme from a URI. Returns <c>null</c> if the URI is null.
    /// </summary>
    public static string? GetScheme(Uri? uri) => uri?.Scheme;

    /// <summary>
    /// Converts this address to a <see cref="Uri"/>.
    /// </summary>
    public Uri ToUri()
    {
        var builder = new UriBuilder(Scheme, Host);
        if (Port.HasValue) builder.Port = Port.Value;
        if (EntityName is not null) builder.Path = EntityName;
        return builder.Uri;
    }

    /// <summary>
    /// Implicit conversion to <see cref="Uri"/>.
    /// </summary>
    public static implicit operator Uri(EmitEndpointAddress address) => address.ToUri();

    /// <inheritdoc />
    public override string ToString() => ToUri().ToString();
}

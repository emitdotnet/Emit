namespace Emit.Kafka.DependencyInjection;

using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

/// <summary>
/// Schema registry configuration for connecting to a Confluent Schema Registry instance.
/// </summary>
public sealed class KafkaSchemaRegistryOptions
{
    // ── Connection ──

    /// <summary>
    /// A comma-separated list of URLs for schema registry instances that are used to register or lookup schemas.
    /// </summary>
    public string? Url { get; set; }

    // ── Timeouts & Retries ──

    /// <summary>
    /// Timeout for requests to the schema registry.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>
    /// Maximum number of retries for a request.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Time to wait for the first retry.
    /// </summary>
    public TimeSpan? RetriesWait { get; set; }

    /// <summary>
    /// Maximum time to wait for any retry.
    /// </summary>
    public TimeSpan? RetriesMaxWait { get; set; }

    // ── Connection Pool ──

    /// <summary>
    /// Maximum number of connections per server.
    /// </summary>
    public int? MaxConnectionsPerServer { get; set; }

    // ── Caching ──

    /// <summary>
    /// Maximum number of schemas the client should cache locally.
    /// </summary>
    public int? MaxCachedSchemas { get; set; }

    /// <summary>
    /// TTL for caches holding latest schemas. Use <see cref="Timeout.InfiniteTimeSpan"/> for no TTL.
    /// </summary>
    public TimeSpan? LatestCacheTtl { get; set; }

    // ── SSL ──

    /// <summary>
    /// File or directory path to CA certificate(s) for verifying the schema registry's key.
    /// </summary>
    public string? SslCaLocation { get; set; }

    /// <summary>
    /// Path to client's keystore (PKCS#12) used for authentication.
    /// </summary>
    public string? SslKeystoreLocation { get; set; }

    /// <summary>
    /// Client's keystore (PKCS#12) password.
    /// </summary>
    public string? SslKeystorePassword { get; set; }

    /// <summary>
    /// Enable or disable SSL server certificate verification.
    /// </summary>
    public bool? EnableSslCertificateVerification { get; set; }

    // ── Basic Auth ──

    /// <summary>
    /// Source of the basic authentication credentials.
    /// </summary>
    public ConfluentSchemaRegistry.AuthCredentialsSource? BasicAuthCredentialsSource { get; set; }

    /// <summary>
    /// Basic auth credentials in the form <c>username:password</c>.
    /// </summary>
    public string? BasicAuthUserInfo { get; set; }

    // ── Bearer Auth ──

    /// <summary>
    /// Bearer authentication token. Used when <see cref="BearerAuthCredentialsSource"/> is set
    /// to <see cref="ConfluentSchemaRegistry.BearerAuthCredentialsSource.StaticToken"/>.
    /// </summary>
    public string? BearerAuthToken { get; set; }

    /// <summary>
    /// OAuth2 client ID for retrieving bearer tokens via OIDC.
    /// </summary>
    public string? BearerAuthClientId { get; set; }

    /// <summary>
    /// OAuth2 client secret for retrieving bearer tokens via OIDC.
    /// </summary>
    public string? BearerAuthClientSecret { get; set; }

    /// <summary>
    /// Source of the bearer authentication credentials.
    /// </summary>
    public ConfluentSchemaRegistry.BearerAuthCredentialsSource? BearerAuthCredentialsSource { get; set; }

    /// <summary>
    /// OAuth2/OIDC token endpoint URL for retrieving bearer tokens.
    /// </summary>
    public string? BearerAuthTokenEndpointUrl { get; set; }

    /// <summary>
    /// OAuth2 scope for retrieving bearer tokens.
    /// </summary>
    public string? BearerAuthScope { get; set; }

    /// <summary>
    /// Applies non-null settings onto a <see cref="ConfluentSchemaRegistry.SchemaRegistryConfig"/>.
    /// </summary>
    internal void ApplyTo(ConfluentSchemaRegistry.SchemaRegistryConfig config)
    {
        if (Url is not null) config.Url = Url;
        if (RequestTimeout.HasValue) config.RequestTimeoutMs = (int)RequestTimeout.Value.TotalMilliseconds;
        if (MaxRetries.HasValue) config.MaxRetries = MaxRetries.Value;
        if (RetriesWait.HasValue) config.RetriesWaitMs = (int)RetriesWait.Value.TotalMilliseconds;
        if (RetriesMaxWait.HasValue) config.RetriesMaxWaitMs = (int)RetriesMaxWait.Value.TotalMilliseconds;
        if (MaxConnectionsPerServer.HasValue) config.MaxConnectionsPerServer = MaxConnectionsPerServer.Value;
        if (MaxCachedSchemas.HasValue) config.MaxCachedSchemas = MaxCachedSchemas.Value;
        if (LatestCacheTtl.HasValue) config.LatestCacheTtlSecs = (int)LatestCacheTtl.Value.TotalSeconds;
        if (SslCaLocation is not null) config.SslCaLocation = SslCaLocation;
        if (SslKeystoreLocation is not null) config.SslKeystoreLocation = SslKeystoreLocation;
        if (SslKeystorePassword is not null) config.SslKeystorePassword = SslKeystorePassword;
        if (EnableSslCertificateVerification.HasValue) config.EnableSslCertificateVerification = EnableSslCertificateVerification.Value;
        if (BasicAuthCredentialsSource.HasValue) config.BasicAuthCredentialsSource = BasicAuthCredentialsSource.Value;
        if (BasicAuthUserInfo is not null) config.BasicAuthUserInfo = BasicAuthUserInfo;
        if (BearerAuthToken is not null) config.BearerAuthToken = BearerAuthToken;
        if (BearerAuthClientId is not null) config.BearerAuthClientId = BearerAuthClientId;
        if (BearerAuthClientSecret is not null) config.BearerAuthClientSecret = BearerAuthClientSecret;
        if (BearerAuthCredentialsSource.HasValue) config.BearerAuthCredentialsSource = BearerAuthCredentialsSource.Value;
        if (BearerAuthTokenEndpointUrl is not null) config.BearerAuthTokenEndpointUrl = BearerAuthTokenEndpointUrl;
        if (BearerAuthScope is not null) config.BearerAuthScope = BearerAuthScope;
    }
}

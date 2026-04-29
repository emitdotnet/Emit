namespace Emit.Kafka.DependencyInjection;

using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Shared Kafka client configuration applied to both producers and consumers.
/// </summary>
public sealed class KafkaClientOptions
{
    // ── Connection ──

    /// <summary>
    /// Initial list of brokers as a CSV list of broker host or host:port.
    /// </summary>
    public string? BootstrapServers { get; set; }

    /// <summary>
    /// Client identifier string.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// A rack identifier for this client. This can be any string value which indicates where this client
    /// is physically located. It corresponds with the broker config <c>broker.rack</c>.
    /// </summary>
    public string? ClientRack { get; set; }

    // ── Security ──

    /// <summary>
    /// Protocol used to communicate with brokers.
    /// </summary>
    public ConfluentKafka.SecurityProtocol? SecurityProtocol { get; set; }

    /// <summary>
    /// SASL mechanism to use for authentication.
    /// Supported: GSSAPI, PLAIN, SCRAM-SHA-256, SCRAM-SHA-512, OAUTHBEARER.
    /// </summary>
    public ConfluentKafka.SaslMechanism? SaslMechanism { get; set; }

    /// <summary>
    /// SASL username for use with the PLAIN and SASL-SCRAM-.. mechanisms.
    /// </summary>
    public string? SaslUsername { get; set; }

    /// <summary>
    /// SASL password for use with the PLAIN and SASL-SCRAM-.. mechanism.
    /// </summary>
    public string? SaslPassword { get; set; }

    /// <summary>
    /// Set to <see cref="ConfluentKafka.SaslOauthbearerMethod.Oidc"/> to use the OIDC token provider,
    /// or <see cref="ConfluentKafka.SaslOauthbearerMethod.Default"/> for the default callback-based provider.
    /// </summary>
    public ConfluentKafka.SaslOauthbearerMethod? SaslOauthbearerMethod { get; set; }

    /// <summary>
    /// Public identifier for the application, used as the client_id for retrieving OAUTHBEARER tokens via OIDC.
    /// </summary>
    public string? SaslOauthbearerClientId { get; set; }

    /// <summary>
    /// Client secret for the application, used to retrieve OAUTHBEARER tokens via OIDC.
    /// </summary>
    public string? SaslOauthbearerClientSecret { get; set; }

    /// <summary>
    /// OAuth/OIDC issuer token endpoint URL used to retrieve OAUTHBEARER tokens.
    /// </summary>
    public string? SaslOauthbearerTokenEndpointUrl { get; set; }

    /// <summary>
    /// Client use this to specify the scope of the access request to the broker.
    /// </summary>
    public string? SaslOauthbearerScope { get; set; }

    /// <summary>
    /// Allow additional information to be provided to the broker in the SASL/OAUTHBEARER
    /// initial client response as comma-separated <c>key=value</c> pairs.
    /// </summary>
    public string? SaslOauthbearerExtensions { get; set; }

    /// <summary>
    /// File or directory path to CA certificate(s) for verifying the broker's key.
    /// Defaults: On Windows the system's CA certificates are automatically looked up in the Windows Root certificate store.
    /// On Linux install the distribution's ca-certificates package.
    /// </summary>
    public string? SslCaLocation { get; set; }

    /// <summary>
    /// Path to client's public key (PEM) used for authentication.
    /// </summary>
    public string? SslCertificateLocation { get; set; }

    /// <summary>
    /// Path to client's private key (PEM) used for authentication.
    /// </summary>
    public string? SslKeyLocation { get; set; }

    /// <summary>
    /// Private key passphrase (for use with <see cref="SslKeyLocation"/>).
    /// </summary>
    public string? SslKeyPassword { get; set; }

    /// <summary>
    /// Endpoint identification algorithm to validate broker hostname using broker certificate.
    /// Set to <see cref="ConfluentKafka.SslEndpointIdentificationAlgorithm.Https"/> to verify that the broker
    /// hostname matches the CN or SAN in the broker's certificate.
    /// Set to <see cref="ConfluentKafka.SslEndpointIdentificationAlgorithm.None"/> to disable hostname verification.
    /// </summary>
    public ConfluentKafka.SslEndpointIdentificationAlgorithm? SslEndpointIdentificationAlgorithm { get; set; }

    /// <summary>
    /// Enable or disable SSL server certificate verification.
    /// Disabling verification is insecure and should only be used for development/testing.
    /// </summary>
    public bool? EnableSslCertificateVerification { get; set; }

    // ── Timeouts ──

    /// <summary>
    /// Default timeout for network requests.
    /// Producer: ProduceRequests will use the lesser value of this and remaining message timeout for the first message in the batch.
    /// Consumer: FetchRequests will use fetch.wait.max + this value.
    /// Admin: Admin requests will use this value.
    /// </summary>
    public TimeSpan? SocketTimeout { get; set; }

    /// <summary>
    /// Maximum time allowed for broker connection setup (TCP connection + SSL/SASL handshake).
    /// If the connection to the broker is not fully functional after this timeout the connection will be closed and retried.
    /// </summary>
    public TimeSpan? SocketConnectionSetupTimeout { get; set; }

    /// <summary>
    /// Close broker connections after the specified time of inactivity. Disable with <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan? ConnectionsMaxIdle { get; set; }

    /// <summary>
    /// Metadata cache max age.
    /// </summary>
    public TimeSpan? MetadataMaxAge { get; set; }

    /// <summary>
    /// Period of time at which topic and broker metadata is refreshed in order to proactively discover
    /// any new brokers, topics, partitions, or partition leader changes. Use <c>TimeSpan.FromMilliseconds(-1)</c>
    /// to disable the intervalled refresh (not recommended). The refresh is triggered when a metadata request fails.
    /// </summary>
    public TimeSpan? TopicMetadataRefreshInterval { get; set; }

    /// <summary>
    /// The initial time to wait before reconnecting to a broker after the connection has been closed.
    /// The time is increased exponentially until <see cref="ReconnectBackoffMax"/> is reached.
    /// -25% to +50% jitter is applied to each reconnect backoff.
    /// A value of <see cref="TimeSpan.Zero"/> disables the backoff and reconnects immediately.
    /// </summary>
    public TimeSpan? ReconnectBackoff { get; set; }

    /// <summary>
    /// The maximum time to wait before reconnecting to a broker after the connection has been closed.
    /// </summary>
    public TimeSpan? ReconnectBackoffMax { get; set; }

    /// <summary>
    /// The backoff time before retrying a protocol request. Increases exponentially
    /// until <see cref="RetryBackoffMax"/> is reached, with +/- 20% jitter.
    /// </summary>
    public TimeSpan? RetryBackoff { get; set; }

    /// <summary>
    /// The maximum backoff time before retrying a protocol request.
    /// </summary>
    public TimeSpan? RetryBackoffMax { get; set; }

    /// <summary>
    /// librdkafka statistics emit interval. The granularity is 1000ms.
    /// A value of <see cref="TimeSpan.Zero"/> disables statistics.
    /// </summary>
    public TimeSpan? StatisticsInterval { get; set; }

    // ── Other ──

    /// <summary>
    /// Enable TCP keep-alives (SO_KEEPALIVE) on broker sockets.
    /// </summary>
    public bool? SocketKeepaliveEnable { get; set; }

    /// <summary>
    /// Maximum Kafka protocol request message size.
    /// Due to differing framing overhead between protocol versions the producer is unable to reliably enforce
    /// a strict max message limit at produce time and may exceed the maximum size by one message in protocol ProduceRequests;
    /// the broker will enforce the topic's <c>max.message.bytes</c> limit.
    /// </summary>
    public int? MessageMaxBytes { get; set; }

    /// <summary>
    /// Maximum Kafka protocol response message size. This serves as a safety precaution to avoid memory
    /// exhaustion in case of protocol hickups. This value must be at least <c>fetch.max.bytes</c> + 512.
    /// </summary>
    public int? ReceiveMessageMaxBytes { get; set; }

    /// <summary>
    /// Maximum number of in-flight requests per broker connection.
    /// This is a generic property applied at the connection level.
    /// A higher value can increase throughput but may reduce ordering guarantees.
    /// </summary>
    public int? MaxInFlightRequestsPerConnection { get; set; }

    /// <summary>
    /// Allowed broker IP address families: any, IPv4, or IPv6.
    /// </summary>
    public ConfluentKafka.BrokerAddressFamily? BrokerAddressFamily { get; set; }

    /// <summary>
    /// Allow automatic topic creation on the broker when subscribing to or assigning non-existent topics.
    /// The broker must also be configured with <c>auto.create.topics.enable=true</c> for this to take effect.
    /// This configuration is only used by the consumer, not the admin client.
    /// </summary>
    public bool? AllowAutoCreateTopics { get; set; }

    /// <summary>
    /// A comma-separated list of debug contexts to enable.
    /// Use <c>"all"</c> to enable everything. Useful for troubleshooting.
    /// Example: <c>"broker,topic,msg"</c>.
    /// </summary>
    public string? Debug { get; set; }

    /// <summary>
    /// Additional librdkafka configuration properties to pass through as raw key-value pairs.
    /// Use this for settings not exposed as typed properties.
    /// Invalid keys will cause an exception when the client is built.
    /// Typed properties take precedence over entries in this dictionary.
    /// </summary>
    public Dictionary<string, string>? AdditionalProperties { get; set; }

    /// <summary>
    /// Applies non-null settings onto a <see cref="ConfluentKafka.ClientConfig"/>.
    /// </summary>
    internal void ApplyTo(ConfluentKafka.ClientConfig config)
    {
        if (AdditionalProperties is { Count: > 0 })
        {
            foreach (var (key, value) in AdditionalProperties)
                config.Set(key, value);
        }

        // Connection
        if (BootstrapServers is not null) config.BootstrapServers = BootstrapServers;
        if (ClientId is not null) config.ClientId = ClientId;
        if (ClientRack is not null) config.ClientRack = ClientRack;

        // Security
        if (SecurityProtocol.HasValue) config.SecurityProtocol = SecurityProtocol.Value;
        if (SaslMechanism.HasValue) config.SaslMechanism = SaslMechanism.Value;
        if (SaslUsername is not null) config.SaslUsername = SaslUsername;
        if (SaslPassword is not null) config.SaslPassword = SaslPassword;
        if (SaslOauthbearerMethod.HasValue) config.SaslOauthbearerMethod = SaslOauthbearerMethod.Value;
        if (SaslOauthbearerClientId is not null) config.SaslOauthbearerClientId = SaslOauthbearerClientId;
        if (SaslOauthbearerClientSecret is not null) config.SaslOauthbearerClientSecret = SaslOauthbearerClientSecret;
        if (SaslOauthbearerTokenEndpointUrl is not null) config.SaslOauthbearerTokenEndpointUrl = SaslOauthbearerTokenEndpointUrl;
        if (SaslOauthbearerScope is not null) config.SaslOauthbearerScope = SaslOauthbearerScope;
        if (SaslOauthbearerExtensions is not null) config.SaslOauthbearerExtensions = SaslOauthbearerExtensions;
        if (SslCaLocation is not null) config.SslCaLocation = SslCaLocation;
        if (SslCertificateLocation is not null) config.SslCertificateLocation = SslCertificateLocation;
        if (SslKeyLocation is not null) config.SslKeyLocation = SslKeyLocation;
        if (SslKeyPassword is not null) config.SslKeyPassword = SslKeyPassword;
        if (SslEndpointIdentificationAlgorithm.HasValue) config.SslEndpointIdentificationAlgorithm = SslEndpointIdentificationAlgorithm.Value;
        if (EnableSslCertificateVerification.HasValue) config.EnableSslCertificateVerification = EnableSslCertificateVerification.Value;

        // Timeouts
        if (SocketTimeout.HasValue) config.SocketTimeoutMs = (int)SocketTimeout.Value.TotalMilliseconds;
        if (SocketConnectionSetupTimeout.HasValue) config.SocketConnectionSetupTimeoutMs = (int)SocketConnectionSetupTimeout.Value.TotalMilliseconds;
        if (ConnectionsMaxIdle.HasValue) config.ConnectionsMaxIdleMs = (int)ConnectionsMaxIdle.Value.TotalMilliseconds;
        if (MetadataMaxAge.HasValue) config.MetadataMaxAgeMs = (int)MetadataMaxAge.Value.TotalMilliseconds;
        if (TopicMetadataRefreshInterval.HasValue) config.TopicMetadataRefreshIntervalMs = (int)TopicMetadataRefreshInterval.Value.TotalMilliseconds;
        if (ReconnectBackoff.HasValue) config.ReconnectBackoffMs = (int)ReconnectBackoff.Value.TotalMilliseconds;
        if (ReconnectBackoffMax.HasValue) config.ReconnectBackoffMaxMs = (int)ReconnectBackoffMax.Value.TotalMilliseconds;
        if (RetryBackoff.HasValue) config.RetryBackoffMs = (int)RetryBackoff.Value.TotalMilliseconds;
        if (RetryBackoffMax.HasValue) config.RetryBackoffMaxMs = (int)RetryBackoffMax.Value.TotalMilliseconds;
        if (StatisticsInterval.HasValue) config.StatisticsIntervalMs = (int)StatisticsInterval.Value.TotalMilliseconds;

        // Other
        if (SocketKeepaliveEnable.HasValue) config.SocketKeepaliveEnable = SocketKeepaliveEnable.Value;
        if (MessageMaxBytes.HasValue) config.MessageMaxBytes = MessageMaxBytes.Value;
        if (ReceiveMessageMaxBytes.HasValue) config.ReceiveMessageMaxBytes = ReceiveMessageMaxBytes.Value;
        if (MaxInFlightRequestsPerConnection.HasValue) config.MaxInFlight = MaxInFlightRequestsPerConnection.Value;
        if (BrokerAddressFamily.HasValue) config.BrokerAddressFamily = BrokerAddressFamily.Value;
        if (AllowAutoCreateTopics.HasValue) config.AllowAutoCreateTopics = AllowAutoCreateTopics.Value;
        if (Debug is not null) config.Debug = Debug;
    }
}

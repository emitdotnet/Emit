namespace Emit.Kafka.DependencyInjection;

using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Shared Kafka client configuration applied to both producers and consumers.
/// </summary>
public sealed class KafkaClientConfig
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

    // ── Security ──

    /// <summary>
    /// Protocol used to communicate with brokers.
    /// </summary>
    public ConfluentKafka.SecurityProtocol? SecurityProtocol { get; set; }

    /// <summary>
    /// SASL mechanism to use for authentication.
    /// Supported: GSSAPI, PLAIN, SCRAM-SHA-256, SCRAM-SHA-512.
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

    // ── Timeouts ──

    /// <summary>
    /// Default timeout for network requests.
    /// Producer: ProduceRequests will use the lesser value of this and remaining message timeout for the first message in the batch.
    /// Consumer: FetchRequests will use fetch.wait.max + this value.
    /// Admin: Admin requests will use this value.
    /// </summary>
    public TimeSpan? SocketTimeout { get; set; }

    /// <summary>
    /// Close broker connections after the specified time of inactivity. Disable with <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan? ConnectionsMaxIdle { get; set; }

    /// <summary>
    /// Metadata cache max age.
    /// </summary>
    public TimeSpan? MetadataMaxAge { get; set; }

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
    /// Applies non-null settings onto a <see cref="ConfluentKafka.ClientConfig"/>.
    /// </summary>
    internal void ApplyTo(ConfluentKafka.ClientConfig config)
    {
        if (BootstrapServers is not null) config.BootstrapServers = BootstrapServers;
        if (ClientId is not null) config.ClientId = ClientId;
        if (SecurityProtocol.HasValue) config.SecurityProtocol = SecurityProtocol.Value;
        if (SaslMechanism.HasValue) config.SaslMechanism = SaslMechanism.Value;
        if (SaslUsername is not null) config.SaslUsername = SaslUsername;
        if (SaslPassword is not null) config.SaslPassword = SaslPassword;
        if (SslCaLocation is not null) config.SslCaLocation = SslCaLocation;
        if (SslCertificateLocation is not null) config.SslCertificateLocation = SslCertificateLocation;
        if (SslKeyLocation is not null) config.SslKeyLocation = SslKeyLocation;
        if (SslKeyPassword is not null) config.SslKeyPassword = SslKeyPassword;
        if (SocketTimeout.HasValue) config.SocketTimeoutMs = (int)SocketTimeout.Value.TotalMilliseconds;
        if (ConnectionsMaxIdle.HasValue) config.ConnectionsMaxIdleMs = (int)ConnectionsMaxIdle.Value.TotalMilliseconds;
        if (MetadataMaxAge.HasValue) config.MetadataMaxAgeMs = (int)MetadataMaxAge.Value.TotalMilliseconds;
        if (ReconnectBackoff.HasValue) config.ReconnectBackoffMs = (int)ReconnectBackoff.Value.TotalMilliseconds;
        if (ReconnectBackoffMax.HasValue) config.ReconnectBackoffMaxMs = (int)ReconnectBackoffMax.Value.TotalMilliseconds;
        if (StatisticsInterval.HasValue) config.StatisticsIntervalMs = (int)StatisticsInterval.Value.TotalMilliseconds;
        if (SocketKeepaliveEnable.HasValue) config.SocketKeepaliveEnable = SocketKeepaliveEnable.Value;
        if (MessageMaxBytes.HasValue) config.MessageMaxBytes = MessageMaxBytes.Value;
    }
}

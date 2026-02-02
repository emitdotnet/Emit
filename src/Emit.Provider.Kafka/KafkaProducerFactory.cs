namespace Emit.Provider.Kafka;

using Confluent.SchemaRegistry;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Factory for creating and managing lazy Kafka producer instances.
/// </summary>
/// <remarks>
/// <para>
/// The factory manages a lazy <see cref="ConfluentKafka.IProducer{TKey, TValue}"/> instance for each
/// registration key. Producers are only created when first needed, avoiding unnecessary
/// broker connections in test scenarios or when the outbox is empty.
/// </para>
/// <para>
/// Each factory instance is associated with a specific registration key and producer
/// configuration. The factory is registered as a keyed singleton in DI.
/// </para>
/// </remarks>
internal sealed class KafkaProducerFactory : IDisposable
{
    private readonly string registrationKey;
    private readonly ConfluentKafka.ProducerConfig producerConfig;
    private readonly SchemaRegistryConfig? schemaRegistryConfig;
    private readonly ILogger<KafkaProducerFactory> logger;
    private readonly Lazy<ConfluentKafka.IProducer<byte[], byte[]>> lazyProducer;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaProducerFactory"/> class.
    /// </summary>
    /// <param name="registrationKey">The registration key for this producer.</param>
    /// <param name="producerConfig">The Kafka producer configuration.</param>
    /// <param name="schemaRegistryConfig">Optional schema registry configuration.</param>
    /// <param name="logger">The logger.</param>
    public KafkaProducerFactory(
        string registrationKey,
        ConfluentKafka.ProducerConfig producerConfig,
        SchemaRegistryConfig? schemaRegistryConfig,
        ILogger<KafkaProducerFactory> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationKey);
        ArgumentNullException.ThrowIfNull(producerConfig);
        ArgumentNullException.ThrowIfNull(logger);

        this.registrationKey = registrationKey;
        this.producerConfig = producerConfig;
        this.schemaRegistryConfig = schemaRegistryConfig;
        this.logger = logger;
        lazyProducer = new Lazy<ConfluentKafka.IProducer<byte[], byte[]>>(CreateProducer);

        logger.LogDebug(
            "KafkaProducerFactory created for registration key '{RegistrationKey}'",
            registrationKey);
    }

    /// <summary>
    /// Gets the registration key for this producer factory.
    /// </summary>
    internal string RegistrationKey => registrationKey;

    /// <summary>
    /// Gets the Kafka producer, creating it lazily on first access.
    /// </summary>
    /// <returns>The Kafka producer instance.</returns>
    /// <remarks>
    /// The producer is created on first access and cached for subsequent calls.
    /// This lazy construction avoids broker connections until messages need to be sent.
    /// </remarks>
    public ConfluentKafka.IProducer<byte[], byte[]> GetProducer()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return lazyProducer.Value;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (lazyProducer.IsValueCreated)
        {
            try
            {
                logger.LogDebug(
                    "Disposing Kafka producer for registration key '{RegistrationKey}'",
                    registrationKey);

                // IProducer implements IClient which has Dispose via Handle property
                // Cast to IDisposable for safe disposal
                if (lazyProducer.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                logger.LogInformation(
                    "Kafka producer for registration key '{RegistrationKey}' disposed",
                    registrationKey);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error disposing Kafka producer for registration key '{RegistrationKey}'",
                    registrationKey);
            }
        }
        else
        {
            logger.LogDebug(
                "Kafka producer for registration key '{RegistrationKey}' was never created, skipping disposal",
                registrationKey);
        }
    }

    private ConfluentKafka.IProducer<byte[], byte[]> CreateProducer()
    {
        logger.LogDebug(
            "Creating Kafka producer for registration key '{RegistrationKey}' (lazy construction triggered)",
            registrationKey);

        var builder = new ConfluentKafka.ProducerBuilder<byte[], byte[]>(producerConfig);

        // Configure error handler
        builder.SetErrorHandler((producer, error) =>
        {
            if (error.IsFatal)
            {
                logger.LogError(
                    "Fatal Kafka error for registration key '{RegistrationKey}': {Reason} (Code: {Code})",
                    registrationKey, error.Reason, error.Code);
            }
            else
            {
                logger.LogWarning(
                    "Kafka error for registration key '{RegistrationKey}': {Reason} (Code: {Code})",
                    registrationKey, error.Reason, error.Code);
            }
        });

        // Configure log handler
        builder.SetLogHandler((producer, logMessage) =>
        {
            // Map Kafka log level to Microsoft.Extensions.Logging LogLevel
            var level = MapKafkaLogLevel(logMessage.Level);
            switch (level)
            {
                case LogLevel.Trace:
                    logger.LogTrace("Kafka: {Message}", logMessage.Message);
                    break;
                case LogLevel.Debug:
                    logger.LogDebug("Kafka: {Message}", logMessage.Message);
                    break;
                case LogLevel.Information:
                    logger.LogInformation("Kafka: {Message}", logMessage.Message);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning("Kafka: {Message}", logMessage.Message);
                    break;
                case LogLevel.Error:
                    logger.LogError("Kafka: {Message}", logMessage.Message);
                    break;
                case LogLevel.Critical:
                    logger.LogCritical("Kafka: {Message}", logMessage.Message);
                    break;
            }
        });

        var producer = builder.Build();

        logger.LogInformation(
            "Kafka producer created for registration key '{RegistrationKey}': BootstrapServers={BootstrapServers}",
            registrationKey, producerConfig.BootstrapServers);

        return producer;
    }

    private static LogLevel MapKafkaLogLevel(ConfluentKafka.SyslogLevel syslogLevel)
    {
        return syslogLevel switch
        {
            ConfluentKafka.SyslogLevel.Emergency or
            ConfluentKafka.SyslogLevel.Alert or
            ConfluentKafka.SyslogLevel.Critical => LogLevel.Critical,
            ConfluentKafka.SyslogLevel.Error => LogLevel.Error,
            ConfluentKafka.SyslogLevel.Warning or
            ConfluentKafka.SyslogLevel.Notice => LogLevel.Warning,
            ConfluentKafka.SyslogLevel.Info => LogLevel.Information,
            ConfluentKafka.SyslogLevel.Debug => LogLevel.Debug,
            _ => LogLevel.Trace
        };
    }
}

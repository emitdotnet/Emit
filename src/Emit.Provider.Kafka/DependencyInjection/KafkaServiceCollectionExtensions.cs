namespace Emit.Provider.Kafka.DependencyInjection;

using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Extension methods for registering Kafka outbox provider services.
/// </summary>
public static class KafkaServiceCollectionExtensions
{
    /// <summary>
    /// Adds a Kafka producer factory to the service collection.
    /// </summary>
    /// <param name="builder">The Emit builder.</param>
    /// <param name="registrationKey">The registration key for keyed service resolution.</param>
    /// <param name="producerConfig">The Kafka producer configuration.</param>
    /// <param name="schemaRegistryConfig">Optional schema registry configuration.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers a <see cref="KafkaProducerFactory"/> as a keyed singleton,
    /// allowing the <see cref="KafkaOutboxProvider"/> to resolve the correct factory
    /// based on the <see cref="Emit.Models.OutboxEntry.RegistrationKey"/>.
    /// </para>
    /// <para>
    /// Call this method once for each Kafka cluster configuration needed.
    /// </para>
    /// </remarks>
    public static EmitBuilder AddKafkaProducerFactory(
        this EmitBuilder builder,
        string registrationKey,
        ProducerConfig producerConfig,
        SchemaRegistryConfig? schemaRegistryConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationKey);
        ArgumentNullException.ThrowIfNull(producerConfig);

        // Register the factory as a keyed singleton
        builder.Services.AddKeyedSingleton(
            registrationKey,
            (sp, key) => new KafkaProducerFactory(
                registrationKey,
                producerConfig,
                schemaRegistryConfig,
                sp.GetRequiredService<ILogger<KafkaProducerFactory>>()));

        // Ensure the provider is registered (idempotent)
        EnsureProviderRegistered(builder.Services);

        return builder;
    }

    /// <summary>
    /// Adds a default (unnamed) Kafka producer factory to the service collection.
    /// </summary>
    /// <param name="builder">The Emit builder.</param>
    /// <param name="producerConfig">The Kafka producer configuration.</param>
    /// <param name="schemaRegistryConfig">Optional schema registry configuration.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <remarks>
    /// This is equivalent to calling <see cref="AddKafkaProducerFactory(EmitBuilder, string, ProducerConfig, SchemaRegistryConfig?)"/>
    /// with <see cref="EmitConstants.DefaultRegistrationKey"/> as the key.
    /// </remarks>
    public static EmitBuilder AddKafkaProducerFactory(
        this EmitBuilder builder,
        ProducerConfig producerConfig,
        SchemaRegistryConfig? schemaRegistryConfig = null)
    {
        return AddKafkaProducerFactory(
            builder,
            EmitConstants.DefaultRegistrationKey,
            producerConfig,
            schemaRegistryConfig);
    }

    private static void EnsureProviderRegistered(IServiceCollection services)
    {
        // Check if already registered to avoid duplicates
        if (services.Any(d => d.ServiceType == typeof(IOutboxProvider) &&
                              d.ImplementationType == typeof(KafkaOutboxProvider)))
        {
            return;
        }

        // Register KafkaOutboxProvider as a singleton
        services.AddSingleton<IOutboxProvider, KafkaOutboxProvider>();
    }
}

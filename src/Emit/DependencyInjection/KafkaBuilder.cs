namespace Emit.DependencyInjection;

using Emit.Resilience;

/// <summary>
/// Builder for configuring a Kafka outbox provider.
/// </summary>
/// <remarks>
/// <para>
/// Use this builder to configure Kafka-specific settings including schema registry,
/// producer registrations, and provider-level resilience policies.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// builder.AddKafka((sp, kafka) =&gt;
/// {
///     kafka.ConfigureResilience(policy =&gt; policy
///         .WithRetry(3, BackoffStrategy.Exponential, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)));
///
///     kafka.AddProducer&lt;string, OrderCreated&gt;(producer =&gt;
///     {
///         producer.ProducerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };
///     });
/// });
/// </code>
/// </para>
/// </remarks>
public sealed class KafkaBuilder
{
    private readonly IServiceProvider serviceProvider;
    private readonly string registrationKey;
    private readonly List<object> producerRegistrations = [];
    private Action<ResiliencePolicyBuilder>? resilienceConfiguration;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaBuilder"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="registrationKey">The registration key (sentinel for default, or user-specified for named).</param>
    internal KafkaBuilder(IServiceProvider serviceProvider, string registrationKey)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.registrationKey = registrationKey ?? throw new ArgumentNullException(nameof(registrationKey));
    }

    /// <summary>
    /// Gets the registration key for this Kafka provider.
    /// </summary>
    internal string RegistrationKey => registrationKey;

    /// <summary>
    /// Gets the service provider.
    /// </summary>
    internal IServiceProvider ServiceProvider => serviceProvider;

    /// <summary>
    /// Gets the registered producer configurations.
    /// </summary>
    internal IReadOnlyList<object> ProducerRegistrations => producerRegistrations;

    /// <summary>
    /// Gets a value indicating whether this is the default (unnamed) registration.
    /// </summary>
    public bool IsDefault => registrationKey == EmitConstants.DefaultRegistrationKey;

    /// <summary>
    /// Configures the provider-level resilience policy.
    /// </summary>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// The provider-level policy applies to all producers registered through this builder
    /// unless overridden at the producer level.
    /// </remarks>
    public KafkaBuilder ConfigureResilience(Action<ResiliencePolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        resilienceConfiguration = configure;
        return this;
    }

    /// <summary>
    /// Registers a producer for the specified key and value types.
    /// </summary>
    /// <typeparam name="TKey">The message key type.</typeparam>
    /// <typeparam name="TValue">The message value type.</typeparam>
    /// <param name="configure">The configuration action for the producer.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// The producer will be registered as an <c>IProducer&lt;TKey, TValue&gt;</c> service.
    /// For default (unnamed) Kafka registrations, the producer is available via standard
    /// constructor injection. For named registrations, use <c>[FromKeyedServices("key")]</c>.
    /// </para>
    /// <para>
    /// The registered producer is an Emit wrapper that enqueues messages to the outbox
    /// instead of producing directly to Kafka. The actual Kafka production happens
    /// asynchronously via the outbox worker.
    /// </para>
    /// </remarks>
    public KafkaBuilder AddProducer<TKey, TValue>(Action<ProducerBuilder<TKey, TValue>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var producerBuilder = new ProducerBuilder<TKey, TValue>(registrationKey);
        configure(producerBuilder);
        producerRegistrations.Add(producerBuilder);

        return this;
    }

    /// <summary>
    /// Builds the provider-level resilience policy from the configured settings.
    /// </summary>
    /// <returns>The provider-level resilience policy, or null if not configured.</returns>
    internal ResiliencePolicy? BuildResiliencePolicy()
    {
        if (resilienceConfiguration is null)
        {
            return null;
        }

        var builder = new ResiliencePolicyBuilder();
        resilienceConfiguration(builder);
        return builder.Build();
    }
}

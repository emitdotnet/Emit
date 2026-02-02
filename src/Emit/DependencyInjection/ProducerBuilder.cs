namespace Emit.DependencyInjection;

using Emit.Resilience;

/// <summary>
/// Builder for configuring a Kafka producer registration.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
/// <remarks>
/// <para>
/// Use this builder to configure producer-specific settings including Confluent.Kafka
/// <c>ProducerConfig</c>, serializers, and producer-level resilience policies.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// kafka.AddProducer&lt;string, OrderCreated&gt;(producer =&gt;
/// {
///     producer.Topic = "orders";
///     producer.ConfigureResilience(policy =&gt; policy
///         .WithRetry(2, BackoffStrategy.FixedInterval, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
/// });
/// </code>
/// </para>
/// </remarks>
public sealed class ProducerBuilder<TKey, TValue>
{
    private readonly string registrationKey;
    private Action<ResiliencePolicyBuilder>? resilienceConfiguration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProducerBuilder{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="registrationKey">The registration key from the parent KafkaBuilder.</param>
    internal ProducerBuilder(string registrationKey)
    {
        this.registrationKey = registrationKey;
    }

    /// <summary>
    /// Gets the registration key.
    /// </summary>
    internal string RegistrationKey => registrationKey;

    /// <summary>
    /// Gets or sets the default topic for this producer.
    /// </summary>
    /// <remarks>
    /// If not specified, the topic must be provided on each <c>Produce</c>/<c>ProduceAsync</c> call.
    /// </remarks>
    public string? Topic { get; set; }

    /// <summary>
    /// Gets or sets the cluster identifier used to build the group key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cluster identifier is combined with the topic name to form the outbox entry's
    /// group key (<c>cluster:topic</c>). This ensures strict ordering per topic within a cluster.
    /// </para>
    /// <para>
    /// If not specified, a default identifier is derived from the bootstrap servers.
    /// </para>
    /// </remarks>
    public string? ClusterIdentifier { get; set; }

    /// <summary>
    /// Configures the producer-level resilience policy.
    /// </summary>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// The producer-level policy is the most specific and takes precedence over
    /// provider-level and global-level policies.
    /// </remarks>
    public ProducerBuilder<TKey, TValue> ConfigureResilience(Action<ResiliencePolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        resilienceConfiguration = configure;
        return this;
    }

    /// <summary>
    /// Builds the producer-level resilience policy from the configured settings.
    /// </summary>
    /// <returns>The producer-level resilience policy, or null if not configured.</returns>
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

    /// <summary>
    /// Gets a unique key identifying this producer registration.
    /// </summary>
    /// <returns>A key combining the registration key and type information.</returns>
    internal string GetProducerKey() =>
        $"{registrationKey}:{typeof(TKey).FullName}:{typeof(TValue).FullName}";
}

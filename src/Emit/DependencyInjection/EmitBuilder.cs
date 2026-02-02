namespace Emit.DependencyInjection;

using Emit.Configuration;
using Emit.Resilience;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builder for configuring Emit services.
/// </summary>
/// <remarks>
/// <para>
/// Use this builder to configure the outbox persistence provider, outbox providers (e.g., Kafka),
/// resilience policies, and worker options.
/// </para>
/// <para>
/// <b>Persistence Provider Constraint:</b>
/// Exactly one persistence provider (MongoDB XOR PostgreSQL) must be registered.
/// Attempting to register both will throw an exception.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// services.AddEmit(builder =&gt;
/// {
///     builder.ConfigureResilience(policy =&gt; policy
///         .WithRetry(5, BackoffStrategy.Exponential, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2)));
///
///     builder.UseMongoDb((sp, options) =&gt;
///     {
///         options.ConnectionString = "mongodb://localhost:27017";
///         options.DatabaseName = "myapp";
///     });
///
///     builder.AddKafka((sp, kafka) =&gt;
///     {
///         kafka.AddProducer&lt;string, OrderCreated&gt;(producer =&gt;
///         {
///             producer.ProducerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };
///         });
///     });
/// });
/// </code>
/// </para>
/// </remarks>
public sealed class EmitBuilder
{
    private readonly IServiceCollection services;
    private readonly List<Action<IServiceProvider>> kafkaRegistrations = [];
    private Action<ResiliencePolicyBuilder>? globalResilienceConfiguration;
    private bool mongoDbRegistered;
    private bool postgreSqlRegistered;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmitBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal EmitBuilder(IServiceCollection services)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Gets the service collection for registering provider-specific services.
    /// </summary>
    /// <remarks>
    /// This property is intended for use by provider packages (e.g., Emit.Provider.Kafka)
    /// to register their own services.
    /// </remarks>
    public IServiceCollection Services => services;

    /// <summary>
    /// Gets a value indicating whether a MongoDB persistence provider has been registered.
    /// </summary>
    internal bool MongoDbRegistered => mongoDbRegistered;

    /// <summary>
    /// Gets a value indicating whether a PostgreSQL persistence provider has been registered.
    /// </summary>
    internal bool PostgreSqlRegistered => postgreSqlRegistered;

    /// <summary>
    /// Gets the configured Kafka registration actions.
    /// </summary>
    internal IReadOnlyList<Action<IServiceProvider>> KafkaRegistrations => kafkaRegistrations;

    /// <summary>
    /// Gets a value indicating whether at least one outbox provider has been registered.
    /// </summary>
    internal bool HasOutboxProvider => kafkaRegistrations.Count > 0;

    /// <summary>
    /// Configures the global resilience policy.
    /// </summary>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// The global policy applies to all providers and producers unless overridden
    /// at the provider or producer level.
    /// </remarks>
    public EmitBuilder ConfigureResilience(Action<ResiliencePolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        globalResilienceConfiguration = configure;
        return this;
    }

    /// <summary>
    /// Configures MongoDB as the persistence provider.
    /// </summary>
    /// <param name="configure">The configuration action for MongoDB options.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if PostgreSQL has already been registered as the persistence provider.
    /// </exception>
    /// <remarks>
    /// Only one persistence provider can be registered. Use either <c>UseMongoDb</c> or
    /// <c>UsePostgreSql</c>, not both.
    /// </remarks>
    public EmitBuilder UseMongoDb(Action<IServiceProvider, MongoDbOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (postgreSqlRegistered)
        {
            throw new InvalidOperationException(
                "Cannot register MongoDB persistence provider: PostgreSQL persistence provider has already been registered. " +
                "Only one persistence provider can be used. Choose either UseMongoDb() or UsePostgreSql(), not both.");
        }

        mongoDbRegistered = true;
        // Actual MongoDB service registration will be done by Emit.Persistence.MongoDB
        return this;
    }

    /// <summary>
    /// Configures PostgreSQL as the persistence provider.
    /// </summary>
    /// <param name="configure">The configuration action for PostgreSQL options.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if MongoDB has already been registered as the persistence provider.
    /// </exception>
    /// <remarks>
    /// Only one persistence provider can be registered. Use either <c>UseMongoDb</c> or
    /// <c>UsePostgreSql</c>, not both.
    /// </remarks>
    public EmitBuilder UsePostgreSql(Action<IServiceProvider, PostgreSqlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (mongoDbRegistered)
        {
            throw new InvalidOperationException(
                "Cannot register PostgreSQL persistence provider: MongoDB persistence provider has already been registered. " +
                "Only one persistence provider can be used. Choose either UseMongoDb() or UsePostgreSql(), not both.");
        }

        postgreSqlRegistered = true;
        // Actual PostgreSQL service registration will be done by Emit.Persistence.PostgreSQL
        return this;
    }

    /// <summary>
    /// Adds a default (unnamed) Kafka outbox provider.
    /// </summary>
    /// <param name="configure">The configuration action for the Kafka provider.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Producers registered through this method are available as standard DI services
    /// (without keyed resolution). Users inject them using normal constructor injection:
    /// <c>IProducer&lt;string, OrderCreated&gt; producer</c>
    /// </para>
    /// <para>
    /// Multiple default Kafka registrations are allowed (though typically not needed).
    /// Use <see cref="AddKafka(string, Action{IServiceProvider, KafkaBuilder})"/> for
    /// named registrations that require keyed service resolution.
    /// </para>
    /// </remarks>
    public EmitBuilder AddKafka(Action<IServiceProvider, KafkaBuilder> configure)
    {
        return AddKafka(EmitConstants.DefaultRegistrationKey, configure);
    }

    /// <summary>
    /// Adds a named Kafka outbox provider.
    /// </summary>
    /// <param name="key">The registration key for keyed service resolution.</param>
    /// <param name="configure">The configuration action for the Kafka provider.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Producers registered through this method are available as keyed DI services.
    /// Users resolve them using:
    /// <c>[FromKeyedServices("analytics")] IProducer&lt;string, string&gt; producer</c>
    /// </para>
    /// <para>
    /// Named registrations are useful when connecting to multiple Kafka clusters,
    /// each with its own configuration.
    /// </para>
    /// </remarks>
    public EmitBuilder AddKafka(string key, Action<IServiceProvider, KafkaBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(configure);

        kafkaRegistrations.Add(sp =>
        {
            var kafkaBuilder = new KafkaBuilder(sp, key);
            configure(sp, kafkaBuilder);
        });

        return this;
    }

    /// <summary>
    /// Builds the global resilience policy from the configured settings.
    /// </summary>
    /// <returns>The global resilience policy, or null if not configured.</returns>
    internal ResiliencePolicy? BuildGlobalResiliencePolicy()
    {
        if (globalResilienceConfiguration is null)
        {
            return null;
        }

        var builder = new ResiliencePolicyBuilder();
        globalResilienceConfiguration(builder);
        return builder.Build();
    }

    /// <summary>
    /// Validates the builder configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no persistence provider is registered or if no outbox provider is registered.
    /// </exception>
    internal void Validate()
    {
        if (!mongoDbRegistered && !postgreSqlRegistered)
        {
            throw new InvalidOperationException(
                "No persistence provider has been registered. " +
                "You must call either UseMongoDb() or UsePostgreSql() to configure the outbox storage.");
        }

        if (!HasOutboxProvider)
        {
            throw new InvalidOperationException(
                "No outbox provider has been registered. " +
                "You must register at least one outbox provider (e.g., AddKafka()) to use Emit.");
        }
    }
}

/// <summary>
/// Configuration options for MongoDB persistence.
/// </summary>
public sealed class MongoDbOptions
{
    /// <summary>
    /// Gets or sets the MongoDB connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the database name.
    /// </summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the outbox collection name.
    /// </summary>
    public string CollectionName { get; set; } = "outbox";

    /// <summary>
    /// Gets or sets the counter collection name for sequence number generation.
    /// </summary>
    public string CounterCollectionName { get; set; } = "outbox_counters";

    /// <summary>
    /// Gets or sets the lease collection name.
    /// </summary>
    public string LeaseCollectionName { get; set; } = "outbox_lease";
}

/// <summary>
/// Configuration options for PostgreSQL persistence.
/// </summary>
public sealed class PostgreSqlOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the database schema.
    /// </summary>
    public string Schema { get; set; } = "public";

    /// <summary>
    /// Gets or sets the outbox table name.
    /// </summary>
    public string TableName { get; set; } = "outbox";

    /// <summary>
    /// Gets or sets the lease table name.
    /// </summary>
    public string LeaseTableName { get; set; } = "outbox_lease";
}

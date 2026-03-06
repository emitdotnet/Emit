namespace Emit.Kafka.Tests.TestInfrastructure;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Kafka;
using Xunit;

/// <summary>
/// Fixture that provides shared Kafka and Schema Registry containers. The underlying
/// containers are static (started once per test run). Each test class gets its own fixture
/// instance via <c>IClassFixture</c> but shares the same containers. Ryuk handles cleanup.
/// </summary>
/// <remarks>
/// <para>
/// Kafka runs in KRaft mode (no Zookeeper). Schema Registry connects
/// to Kafka via a shared Docker network using the BROKER listener on port 9093.
/// </para>
/// <para>
/// Tests should use unique topic names for isolation since all tests
/// share the same Kafka broker.
/// </para>
/// </remarks>
public sealed class KafkaContainerFixture : IAsyncLifetime
{
    private const int SchemaRegistryPort = 8081;

    private static readonly INetwork Network = new NetworkBuilder().Build();

    private static readonly KafkaContainer KafkaContainer = new KafkaBuilder("confluentinc/cp-kafka:7.6.0")
        .WithNetwork(Network)
        .WithNetworkAliases("kafka")
        .Build();

    private static readonly IContainer SchemaRegistryContainer = new ContainerBuilder("confluentinc/cp-schema-registry:7.6.0")
        .WithNetwork(Network)
        .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
        .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS", "kafka:9093")
        .WithEnvironment("SCHEMA_REGISTRY_LISTENERS", $"http://0.0.0.0:{SchemaRegistryPort}")
        .WithPortBinding(SchemaRegistryPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r
                .ForPort(SchemaRegistryPort)
                .ForPath("/subjects")))
        .Build();

    private static readonly Lazy<Task> StartTask = new(StartContainersAsync);

    /// <summary>
    /// Gets the Kafka bootstrap servers address for client connections.
    /// </summary>
    public string BootstrapServers => KafkaContainer.GetBootstrapAddress();

    /// <summary>
    /// Gets the Schema Registry URL for schema operations.
    /// </summary>
    public string SchemaRegistryUrl =>
        $"http://{SchemaRegistryContainer.Hostname}:{SchemaRegistryContainer.GetMappedPublicPort(SchemaRegistryPort)}";

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await StartTask.Value.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task StartContainersAsync()
    {
        await Network.CreateAsync().ConfigureAwait(false);
        await KafkaContainer.StartAsync().ConfigureAwait(false);
        await SchemaRegistryContainer.StartAsync().ConfigureAwait(false);
    }
}

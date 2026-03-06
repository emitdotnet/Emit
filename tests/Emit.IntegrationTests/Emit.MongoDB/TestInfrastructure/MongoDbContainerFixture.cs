namespace Emit.MongoDB.Tests.TestInfrastructure;

using Testcontainers.MongoDb;
using Xunit;

/// <summary>
/// Fixture that provides a shared MongoDB container. The underlying container is static
/// (started once per test run). Each test class gets its own fixture instance via
/// <c>IClassFixture</c> but shares the same container. Ryuk handles cleanup on process exit.
/// </summary>
/// <remarks>
/// The container uses a single-node replica set to support transactions.
/// </remarks>
public sealed class MongoDbContainerFixture : IAsyncLifetime
{
    private static readonly MongoDbContainer Container = new MongoDbBuilder("mongo:7.0")
        .WithReplicaSet()
        .Build();

    private static readonly Lazy<Task> StartTask = new(() => Container.StartAsync());

    /// <summary>
    /// Gets the MongoDB connection string from the shared container.
    /// </summary>
    public string ConnectionString => Container.GetConnectionString();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await StartTask.Value.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task DisposeAsync() => Task.CompletedTask;
}

namespace Emit.EntityFrameworkCore.Tests.TestInfrastructure;

using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Fixture that provides a shared PostgreSQL container. The underlying container is static
/// (started once per test run). Each test class gets its own fixture instance via
/// <c>IClassFixture</c> but shares the same container. Ryuk handles cleanup on process exit.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private static readonly PostgreSqlContainer Container = new PostgreSqlBuilder("postgres:16")
        .Build();

    private static readonly Lazy<Task> StartTask = new(() => Container.StartAsync());

    /// <summary>
    /// Gets the PostgreSQL connection string from the shared container.
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

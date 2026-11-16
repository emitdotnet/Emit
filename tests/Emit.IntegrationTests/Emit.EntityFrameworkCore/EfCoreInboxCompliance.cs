namespace Emit.EntityFrameworkCore.Tests.Inbox;

using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// EF Core (PostgreSQL) + Kafka implementation of <see cref="InboxCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreInboxCompliance(
    PostgreSqlContainerFixture postgresFixture,
    KafkaContainerFixture kafkaFixture)
    : InboxCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    /// <inheritdoc/>
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "inbox");
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await testDb.DropAsync();
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit, TimeSpan retention)
    {
        emit.Services.AddDbContextFactory<IntegrationTestDbContext>(opts =>
            opts.UseNpgsql(testDb.ConnectionString));

        emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
        {
            ef.UseNpgsql();
            ef.UseInbox(options =>
            {
                options.Retention = retention;
                options.CleanupInterval = TimeSpan.FromSeconds(1);
            });
        });
    }

    /// <inheritdoc/>
    protected override async Task<IReadOnlyList<StoredInboxRecord>> ReadInboxRecordsAsync(string messageId)
    {
        await using var connection = new NpgsqlConnection(testDb.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT handler, handled_at, expires_at FROM emit_inbox WHERE message_id = @messageId";
        command.Parameters.AddWithValue("messageId", messageId);

        var records = new List<StoredInboxRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new StoredInboxRecord(
                reader.GetString(0),
                reader.GetFieldValue<DateTime>(1),
                reader.GetFieldValue<DateTime>(2)));
        }

        return records;
    }
}

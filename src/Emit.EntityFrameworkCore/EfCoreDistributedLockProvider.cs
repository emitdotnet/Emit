namespace Emit.EntityFrameworkCore;

using System.Data;
using System.Data.Common;
using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using Emit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// PostgreSQL implementation of <see cref="DistributedLockProviderBase"/>.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class EfCoreDistributedLockProvider<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory,
    IRandomProvider randomProvider,
    LockMetrics lockMetrics,
    ILogger<EfCoreDistributedLockProvider<TDbContext>> logger) : DistributedLockProviderBase(randomProvider, lockMetrics: lockMetrics)
    where TDbContext : DbContext
{
    /// <inheritdoc />
    protected override async Task<bool> TryAcquireCoreAsync(
        string key,
        Guid lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        // Atomic conditional upsert using server-side clock_timestamp().
        // INSERT ON CONFLICT DO UPDATE WHERE expires_at <= clock_timestamp() cannot be
        // expressed as EF Core LINQ — raw SQL is justified here.
        var sql = $"""
            INSERT INTO {TableNames.Locks} (key, lock_id, expires_at)
            VALUES (@key, @lockId, clock_timestamp() + @ttl)
            ON CONFLICT (key) DO UPDATE SET lock_id = @lockId, expires_at = clock_timestamp() + @ttl
            WHERE {TableNames.Locks}.expires_at <= clock_timestamp()
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@key", key);
        AddParameter(command, "@lockId", lockId);
        AddParameter(command, "@ttl", ttl);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    protected override async Task ReleaseCoreAsync(
        string key,
        Guid lockId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.Set<LockEntity>()
                .Where(e => e.Key == key && e.LockId == lockId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to release lock for key '{Key}'. The lock will expire after its TTL.", key);
        }
    }

    /// <inheritdoc />
    protected override async Task<bool> ExtendCoreAsync(
        string key,
        Guid lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        // Uses server-side clock_timestamp() to avoid client/server clock skew on lock expiry.
        var sql = $"""
            UPDATE {TableNames.Locks}
            SET expires_at = clock_timestamp() + @ttl
            WHERE key = @key AND lock_id = @lockId
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@key", key);
        AddParameter(command, "@lockId", lockId);
        AddParameter(command, "@ttl", ttl);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

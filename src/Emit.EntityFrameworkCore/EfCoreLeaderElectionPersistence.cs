namespace Emit.EntityFrameworkCore;

using System.Data;
using System.Data.Common;
using Emit.Abstractions.LeaderElection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// PostgreSQL implementation of <see cref="ILeaderElectionPersistence"/>.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class EfCoreLeaderElectionPersistence<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory,
    ILogger<EfCoreLeaderElectionPersistence<TDbContext>> logger) : ILeaderElectionPersistence
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<HeartbeatResult> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        // 1. Upsert node registration
        await using (var upsertCmd = connection.CreateCommand())
        {
            upsertCmd.CommandText = $"""
                INSERT INTO "{TableNames.Nodes}" (node_id, instance_id, started_at, last_seen_at)
                VALUES (@nodeId, @instanceId, clock_timestamp(), clock_timestamp())
                ON CONFLICT (node_id) DO UPDATE SET last_seen_at = clock_timestamp()
                """;
            AddParameter(upsertCmd, "@nodeId", request.NodeId);
            AddParameter(upsertCmd, "@instanceId", request.InstanceId);
            await upsertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // 2. Atomic CAS for leader election
        await using var casCmd = connection.CreateCommand();
        casCmd.CommandText = $"""
            INSERT INTO "{TableNames.Leader}" (key, node_id, expires_at)
            VALUES ('leader', @nodeId, clock_timestamp() + @lease)
            ON CONFLICT (key) DO UPDATE
                SET node_id = @nodeId, expires_at = clock_timestamp() + @lease
                WHERE "{TableNames.Leader}".node_id = @nodeId
                   OR "{TableNames.Leader}".expires_at <= clock_timestamp()
            RETURNING node_id
            """;
        AddParameter(casCmd, "@nodeId", request.NodeId);
        AddParameter(casCmd, "@lease", request.LeaseDuration);

        await using var reader = await casCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var leaderNodeId = reader.GetGuid(0);
            return new HeartbeatResult(leaderNodeId == request.NodeId, leaderNodeId);
        }

        // No row returned from CAS — another node holds leadership and lease hasn't expired.
        // Query the current leader.
        await reader.CloseAsync().ConfigureAwait(false);

        var currentLeader = await GetCurrentLeaderAsync(connection, cancellationToken).ConfigureAwait(false);
        return new HeartbeatResult(false, currentLeader);
    }

    /// <inheritdoc />
    public async Task ResignLeadershipAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            DELETE FROM "{TableNames.Leader}" WHERE key = 'leader' AND node_id = @nodeId
            """;

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "@nodeId", nodeId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to resign leadership for node {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> RemoveExpiredNodesAsync(
        TimeSpan nodeRegistrationTtl,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            DELETE FROM "{TableNames.Nodes}"
            WHERE last_seen_at < clock_timestamp() - @ttl
            RETURNING node_id
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@ttl", nodeRegistrationTtl);

        List<Guid> removedNodeIds = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            removedNodeIds.Add(reader.GetGuid(0));
        }

        return removedNodeIds;
    }

    /// <inheritdoc />
    public async Task DeregisterNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            DELETE FROM "{TableNames.Nodes}" WHERE node_id = @nodeId
            """;

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "@nodeId", nodeId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to deregister node {NodeId}", nodeId);
        }
    }

    private static async Task<Guid> GetCurrentLeaderAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT node_id FROM "{TableNames.Leader}" WHERE key = 'leader'
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is Guid guid ? guid : Guid.Empty;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetActiveNodeIdsAsync(
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT node_id FROM "{TableNames.Nodes}"
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        List<Guid> nodeIds = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            nodeIds.Add(reader.GetGuid(0));
        }

        return nodeIds;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

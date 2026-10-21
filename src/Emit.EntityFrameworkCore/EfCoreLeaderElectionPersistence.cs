namespace Emit.EntityFrameworkCore;

using System.Data;
using System.Data.Common;
using Emit.Abstractions.LeaderElection;
using Emit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// PostgreSQL implementation of <see cref="ILeaderElectionPersistence"/>.
/// </summary>
/// <remarks>
/// Every service has its own lease row and its own node registrations.
/// </remarks>
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
        ArgumentNullException.ThrowIfNull(request);

        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        // 1. Atomic CAS on the service's lease, unless the heartbeat does not claim it: acquire it if there is
        //    none, renew it if this node holds it, or take it over if it is still recorded with the heartbeat this
        //    node saw go unrenewed
        (Guid NodeId, Guid HeartbeatId, TimeSpan LeaseDuration)? lease = null;
        if (request.ClaimsLease)
        {
            await using var casCmd = connection.CreateCommand();
            casCmd.CommandText = $"""
                INSERT INTO "{TableNames.Leader}" (service_name, node_id, heartbeat_id, lease_duration)
                VALUES (@serviceName, @nodeId, @heartbeatId, @lease)
                ON CONFLICT (service_name) DO UPDATE
                    SET node_id = @nodeId, heartbeat_id = @heartbeatId, lease_duration = @lease
                    WHERE "{TableNames.Leader}".node_id = @nodeId
                       OR "{TableNames.Leader}".heartbeat_id = @takeOver
                RETURNING node_id, heartbeat_id, lease_duration
                """;
            AddParameter(casCmd, "@serviceName", request.ServiceName);
            AddParameter(casCmd, "@nodeId", request.NodeId);
            AddParameter(casCmd, "@heartbeatId", request.HeartbeatId);
            AddParameter(casCmd, "@lease", request.LeaseDuration);
            AddParameter(casCmd, "@takeOver", request.TakeOverLeaseHeartbeatId);

            // No row returned: another node holds the service's lease
            await using var reader = await casCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lease = (reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<TimeSpan>(2));
            }
        }

        lease ??= await GetCurrentLeaderAsync(dbContext, request.ServiceName, cancellationToken).ConfigureAwait(false);

        // 2. Refresh the node registration last, so a node whose heartbeat does not complete is not seen as
        //    alive. Such a node stops its daemons, and the leader must be able to move them once its
        //    registration goes unrenewed.
        await RegisterAsync(connection, request, cancellationToken).ConfigureAwait(false);
        return lease is { } held
            ? new HeartbeatResult(request.ClaimsLease && held.NodeId == request.NodeId, held.NodeId, held.HeartbeatId, held.LeaseDuration)
            : new HeartbeatResult(false, Guid.Empty, Guid.Empty, TimeSpan.Zero);
    }

    private static async Task RegisterAsync(DbConnection connection, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        await using var upsertCmd = connection.CreateCommand();
        upsertCmd.CommandText = $"""
            INSERT INTO "{TableNames.Nodes}" (node_id, service_name, instance_id, daemon_ids, started_at, last_seen_at, heartbeat_id, registration_ttl)
            VALUES (@nodeId, @serviceName, @instanceId, @daemonIds, clock_timestamp(), clock_timestamp(), @heartbeatId, @registrationTtl)
            ON CONFLICT (node_id) DO UPDATE SET
                service_name     = EXCLUDED.service_name,
                instance_id      = EXCLUDED.instance_id,
                daemon_ids       = EXCLUDED.daemon_ids,
                last_seen_at     = clock_timestamp(),
                heartbeat_id     = EXCLUDED.heartbeat_id,
                registration_ttl = EXCLUDED.registration_ttl
            """;
        AddParameter(upsertCmd, "@nodeId", request.NodeId);
        AddParameter(upsertCmd, "@serviceName", request.ServiceName);
        AddParameter(upsertCmd, "@instanceId", request.InstanceId);
        AddParameter(upsertCmd, "@daemonIds", request.DaemonIds.ToArray());
        AddParameter(upsertCmd, "@heartbeatId", request.HeartbeatId);
        AddParameter(upsertCmd, "@registrationTtl", request.NodeTtl);
        await upsertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResignLeadershipAsync(
        string serviceName,
        Guid nodeId,
        IReadOnlyCollection<Guid> heartbeatIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heartbeatIds);

        try
        {
            var ids = heartbeatIds.ToArray();
            await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.Set<LeaderEntity>()
                .Where(e => e.ServiceName == serviceName && e.NodeId == nodeId && ids.Contains(e.HeartbeatId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to resign leadership for node {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> RemoveNodesAsync(
        string serviceName,
        IReadOnlyCollection<NodeRegistration> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0)
        {
            return [];
        }

        // Each registration is removed only if it is still recorded with the heartbeat that was read
        var sql = $"""
            DELETE FROM "{TableNames.Nodes}" AS n
            USING unnest(@nodeIds, @heartbeatIds) AS r(node_id, heartbeat_id)
            WHERE n.service_name = @serviceName
              AND n.node_id = r.node_id
              AND n.heartbeat_id = r.heartbeat_id
            RETURNING n.node_id
            """;

        // One statement removes them all or none; when its outcome is unknown, none is reported and the caller
        // tries again
        try
        {
            await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
            var connection = dbContext.Database.GetDbConnection();
            await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "@serviceName", serviceName);
            AddParameter(command, "@nodeIds", nodes.Select(n => n.NodeId).ToArray());
            AddParameter(command, "@heartbeatIds", nodes.Select(n => n.HeartbeatId).ToArray());

            List<Guid> removedNodeIds = [];
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                removedNodeIds.Add(reader.GetGuid(0));
            }

            return removedNodeIds;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove the registrations of {Count} node(s) of service {ServiceName}", nodes.Count, serviceName);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task DeregisterNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.Set<NodeEntity>()
                .Where(e => e.NodeId == nodeId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to deregister node {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NodeRegistration>> GetNodesAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
        var nodes = await dbContext.Set<NodeEntity>()
            .AsNoTracking()
            .Where(e => e.ServiceName == serviceName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return nodes.ConvertAll(n => new NodeRegistration(n.NodeId, n.DaemonIds, n.HeartbeatId, n.RegistrationTtl));
    }

    private static async Task<(Guid NodeId, Guid HeartbeatId, TimeSpan LeaseDuration)?> GetCurrentLeaderAsync(
        DbContext dbContext,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var lease = await dbContext.Set<LeaderEntity>()
            .AsNoTracking()
            .Where(e => e.ServiceName == serviceName)
            .Select(e => new { e.NodeId, e.HeartbeatId, e.LeaseDuration })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return lease is null ? null : (lease.NodeId, lease.HeartbeatId, lease.LeaseDuration);
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

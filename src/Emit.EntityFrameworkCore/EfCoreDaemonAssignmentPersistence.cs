namespace Emit.EntityFrameworkCore;

using System.Data;
using System.Data.Common;
using Emit.Abstractions.Daemon;
using Emit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

/// <summary>
/// PostgreSQL implementation of <see cref="IDaemonAssignmentPersistence"/>.
/// </summary>
/// <remarks>
/// The service name and daemon identifier form the primary key.
/// </remarks>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class EfCoreDaemonAssignmentPersistence<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory) : IDaemonAssignmentPersistence
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<DaemonAssignment?> AssignAsync(
        string serviceName,
        string daemonId,
        Guid nodeId,
        DaemonAssignment? expected,
        CancellationToken cancellationToken)
    {
        // A first assignment only succeeds while none exists; a reassignment only while the row is still the one read
        var sql = expected is null
            ? $"""
                INSERT INTO "{TableNames.DaemonAssignments}"
                    (service_name, daemon_id, assigned_node_id, generation, state, assigned_at, fencing_key)
                VALUES (@serviceName, @daemonId, @nodeId, 1, 'Assigning', clock_timestamp(), @fencingKey)
                ON CONFLICT (service_name, daemon_id) DO UPDATE SET
                    assigned_node_id = @nodeId,
                    generation       = "{TableNames.DaemonAssignments}".generation + 1,
                    state            = 'Assigning',
                    assigned_at      = clock_timestamp()
                WHERE "{TableNames.DaemonAssignments}".state = '{DaemonAssignmentEntity.ReleasedState}'
                RETURNING daemon_id, assigned_node_id, generation, state, assigned_at, fencing_key
                """
            : $"""
                UPDATE "{TableNames.DaemonAssignments}"
                SET
                    assigned_node_id = @nodeId,
                    generation       = generation + 1,
                    state            = 'Assigning',
                    assigned_at      = clock_timestamp()
                WHERE service_name = @serviceName AND daemon_id = @daemonId
                  AND generation = @expectedGeneration AND state = @expectedState
                RETURNING daemon_id, assigned_node_id, generation, state, assigned_at, fencing_key
                """;

        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@serviceName", serviceName);
        AddParameter(command, "@daemonId", daemonId);
        AddParameter(command, "@nodeId", nodeId);
        if (expected is null)
        {
            AddParameter(command, "@fencingKey", Guid.NewGuid());
        }
        else
        {
            AddParameter(command, "@expectedGeneration", expected.Generation);
            AddParameter(command, "@expectedState", expected.State.ToString());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadAssignment(reader);
    }

    /// <inheritdoc />
    public async Task<DaemonAssignment?> RevokeAsync(
        string serviceName,
        string daemonId,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE "{TableNames.DaemonAssignments}"
            SET
                generation = generation + 1,
                state      = 'Revoking'
            WHERE service_name = @serviceName AND daemon_id = @daemonId AND generation = @expectedGeneration
              AND state <> '{DaemonAssignmentEntity.ReleasedState}'
            RETURNING daemon_id, assigned_node_id, generation, state, assigned_at, fencing_key
            """;

        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@serviceName", serviceName);
        AddParameter(command, "@daemonId", daemonId);
        AddParameter(command, "@expectedGeneration", expectedGeneration);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadAssignment(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);

        var entities = await dbContext.Set<DaemonAssignmentEntity>()
            .AsNoTracking()
            .Where(e => e.ServiceName == serviceName && e.State != DaemonAssignmentEntity.ReleasedState)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(ToAssignment);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(
        string serviceName,
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);

        var entities = await dbContext.Set<DaemonAssignmentEntity>()
            .AsNoTracking()
            .Where(e => e.ServiceName == serviceName && e.AssignedNodeId == nodeId && e.State != DaemonAssignmentEntity.ReleasedState)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(ToAssignment);
    }

    /// <inheritdoc />
    public async Task<bool> AcknowledgeAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);

        var affected = await dbContext.Set<DaemonAssignmentEntity>()
            .Where(e => e.ServiceName == serviceName
                && e.DaemonId == daemonId
                && e.Generation == generation
                && e.State == nameof(DaemonAssignmentState.Assigning))
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.State, nameof(DaemonAssignmentState.Active)),
                cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmDrainAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);

        var affected = await dbContext.Set<DaemonAssignmentEntity>()
            .Where(e => e.ServiceName == serviceName
                && e.DaemonId == daemonId
                && e.Generation == generation
                && e.State == nameof(DaemonAssignmentState.Revoking))
            .ExecuteUpdateAsync(Release, cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);

        var affected = await dbContext.Set<DaemonAssignmentEntity>()
            .Where(e => e.ServiceName == serviceName
                && e.DaemonId == daemonId
                && e.Generation == generation
                && e.State != DaemonAssignmentEntity.ReleasedState)
            .ExecuteUpdateAsync(Release, cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }

    private static void Release(UpdateSettersBuilder<DaemonAssignmentEntity> setters) =>
        setters
            .SetProperty(e => e.State, DaemonAssignmentEntity.ReleasedState)
            .SetProperty(e => e.Generation, e => e.Generation + 1);

    private static DaemonAssignment ReadAssignment(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt64(2),
            Enum.Parse<DaemonAssignmentState>(reader.GetString(3)),
            reader.GetDateTime(4),
            reader.GetGuid(5));

    private static DaemonAssignment ToAssignment(DaemonAssignmentEntity entity) =>
        new(
            entity.DaemonId,
            entity.AssignedNodeId,
            entity.Generation,
            Enum.Parse<DaemonAssignmentState>(entity.State),
            entity.AssignedAt,
            entity.FencingKey);

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

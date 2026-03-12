namespace Emit.EntityFrameworkCore;

using System.Data;
using System.Data.Common;
using Emit.Abstractions.Daemon;
using Emit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// PostgreSQL implementation of <see cref="IDaemonAssignmentPersistence"/>.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class EfCoreDaemonAssignmentPersistence<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory) : IDaemonAssignmentPersistence
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<DaemonAssignment> AssignAsync(
        string daemonId,
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            INSERT INTO "{TableNames.DaemonAssignments}"
                (daemon_id, assigned_node_id, generation, state, assigned_at, drain_deadline)
            VALUES (@daemonId, @nodeId, 1, 'Assigning', clock_timestamp(), NULL)
            ON CONFLICT (daemon_id) DO UPDATE SET
                assigned_node_id = @nodeId,
                generation       = "{TableNames.DaemonAssignments}".generation + 1,
                state            = 'Assigning',
                assigned_at      = clock_timestamp(),
                drain_deadline   = NULL
            RETURNING daemon_id, assigned_node_id, generation, state, assigned_at, drain_deadline
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@daemonId", daemonId);
        AddParameter(command, "@nodeId", nodeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return ReadAssignment(reader);
    }

    /// <inheritdoc />
    public async Task<DaemonAssignment?> RevokeAsync(
        string daemonId,
        TimeSpan drainTimeout,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE "{TableNames.DaemonAssignments}"
            SET
                generation     = generation + 1,
                state          = 'Revoking',
                drain_deadline = clock_timestamp() + @drainTimeout
            WHERE daemon_id = @daemonId
            RETURNING daemon_id, assigned_node_id, generation, state, assigned_at, drain_deadline
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@daemonId", daemonId);
        AddParameter(command, "@drainTimeout", drainTimeout);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadAssignment(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entities = await dbContext.Set<DaemonAssignmentEntity>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(ToAssignment);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entities = await dbContext.Set<DaemonAssignmentEntity>()
            .AsNoTracking()
            .Where(e => e.AssignedNodeId == nodeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(ToAssignment);
    }

    /// <inheritdoc />
    public async Task<bool> AcknowledgeAsync(
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var affected = await dbContext.Set<DaemonAssignmentEntity>()
            .Where(e => e.DaemonId == daemonId
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
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var affected = await dbContext.Set<DaemonAssignmentEntity>()
            .Where(e => e.DaemonId == daemonId
                && e.Generation == generation
                && e.State == nameof(DaemonAssignmentState.Revoking))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }

    private static DaemonAssignment ReadAssignment(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt64(2),
            Enum.Parse<DaemonAssignmentState>(reader.GetString(3)),
            reader.GetDateTime(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5));

    private static DaemonAssignment ToAssignment(DaemonAssignmentEntity entity) =>
        new(
            entity.DaemonId,
            entity.AssignedNodeId,
            entity.Generation,
            Enum.Parse<DaemonAssignmentState>(entity.State),
            entity.AssignedAt,
            entity.DrainDeadline);

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

namespace Emit.EntityFrameworkCore;

using System.Data;
using System.Data.Common;
using Emit.Abstractions.Daemon;
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
            INSERT INTO "{TableNames.DaemonAssignments}" (daemon_id, assigned_node_id, generation, state, assigned_at, drain_deadline)
            VALUES (@daemonId, @nodeId, 1, 'Assigning', clock_timestamp(), NULL)
            ON CONFLICT (daemon_id) DO UPDATE SET
                assigned_node_id = @nodeId,
                generation = "{TableNames.DaemonAssignments}".generation + 1,
                state = 'Assigning',
                assigned_at = clock_timestamp(),
                drain_deadline = NULL
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
            UPDATE "{TableNames.DaemonAssignments}" SET
                generation = generation + 1,
                state = 'Revoking',
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
        var sql = $"""
            SELECT daemon_id, assigned_node_id, generation, state, assigned_at, drain_deadline
            FROM "{TableNames.DaemonAssignments}"
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        List<DaemonAssignment> assignments = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            assignments.Add(ReadAssignment(reader));
        }

        return assignments;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT daemon_id, assigned_node_id, generation, state, assigned_at, drain_deadline
            FROM "{TableNames.DaemonAssignments}"
            WHERE assigned_node_id = @nodeId
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@nodeId", nodeId);

        List<DaemonAssignment> assignments = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            assignments.Add(ReadAssignment(reader));
        }

        return assignments;
    }

    /// <inheritdoc />
    public async Task<bool> AcknowledgeAsync(
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE "{TableNames.DaemonAssignments}"
            SET state = 'Active'
            WHERE daemon_id = @daemonId AND generation = @generation AND state = 'Assigning'
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@daemonId", daemonId);
        AddParameter(command, "@generation", generation);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmDrainAsync(
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            DELETE FROM "{TableNames.DaemonAssignments}"
            WHERE daemon_id = @daemonId AND generation = @generation AND state = 'Revoking'
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@daemonId", daemonId);
        AddParameter(command, "@generation", generation);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    private static DaemonAssignment ReadAssignment(DbDataReader reader)
    {
        var daemonId = reader.GetString(0);
        var assignedNodeId = reader.GetGuid(1);
        var generation = reader.GetInt64(2);
        var state = Enum.Parse<DaemonAssignmentState>(reader.GetString(3));
        var assignedAt = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc);
        var drainDeadline = reader.IsDBNull(5)
            ? (DateTime?)null
            : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc);

        return new DaemonAssignment(daemonId, assignedNodeId, generation, state, assignedAt, drainDeadline);
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

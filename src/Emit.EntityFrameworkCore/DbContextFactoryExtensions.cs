namespace Emit.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>
/// Creates the contexts Emit's own background work runs on.
/// </summary>
internal static class DbContextFactoryExtensions
{
    /// <summary>
    /// Creates a context whose connection only ever reaches the primary, whatever server type the user's connection
    /// string targets: leader election, daemon coordination, locks and outbox delivery must never act on a lagging
    /// standby's view.
    /// </summary>
    public static async Task<TDbContext> CreateOnPrimaryAsync<TDbContext>(
        this IDbContextFactory<TDbContext> dbContextFactory,
        CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var connectionString = new NpgsqlConnectionStringBuilder(connection.ConnectionString);

        // Only a connection string listing several hosts can reach another server than the one it names
        if (connectionString.Host?.Contains(',', StringComparison.Ordinal) == true
            && !string.Equals(connectionString.TargetSessionAttributes, "primary", StringComparison.OrdinalIgnoreCase))
        {
            connectionString.TargetSessionAttributes = "primary";

            // The clone keeps the password and security callbacks of the user's connection
            dbContext.Database.SetDbConnection(connection.CloneWith(connectionString.ConnectionString), contextOwnsConnection: true);
        }

        return dbContext;
    }
}

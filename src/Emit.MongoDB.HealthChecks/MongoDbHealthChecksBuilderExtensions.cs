namespace Emit.MongoDB.HealthChecks;

using Emit.MongoDB.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Extension methods for registering the Emit MongoDB health check.
/// </summary>
public static class MongoDbHealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check that verifies connectivity to MongoDB by running a <c>ping</c> command.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. Defaults to <c>emit-mongodb</c>.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> reported when the check fails.
    /// Defaults to <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Optional tags for filtering health checks.</param>
    /// <param name="timeout">The per-check timeout.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddMongoDb()</c> to have been called on the Emit builder.
    /// The check issues a <c>db.runCommand({ping: 1})</c> against the configured database.
    /// </remarks>
    public static IHealthChecksBuilder AddEmitMongoDB(
        this IHealthChecksBuilder builder,
        string name = "emit-mongodb",
        HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default,
        TimeSpan? timeout = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MongoDbHealthCheck(sp.GetRequiredService<MongoDbContext>()),
            failureStatus,
            tags ?? ["emit", "mongodb"],
            timeout));
    }
}

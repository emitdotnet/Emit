namespace Emit.EntityFrameworkCore.HealthChecks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Extension methods for registering the Emit Entity Framework Core health check.
/// </summary>
public static class EfCoreHealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check that verifies database connectivity using Entity Framework Core.
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The <see cref="DbContext"/> type. Must match the type registered with
    /// <c>AddEntityFrameworkCore&lt;TDbContext&gt;</c> on the Emit builder.
    /// </typeparam>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. Defaults to <c>emit-postgresql</c>.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> reported when the check fails.
    /// Defaults to <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Optional tags for filtering health checks.</param>
    /// <param name="timeout">The per-check timeout.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddEntityFrameworkCore&lt;TDbContext&gt;()</c> to have been called on the Emit builder
    /// and <c>IDbContextFactory&lt;TDbContext&gt;</c> to be registered in the DI container.
    /// </remarks>
    public static IHealthChecksBuilder AddEmitPostgreSQL<TDbContext>(
        this IHealthChecksBuilder builder,
        string name = "emit-postgresql",
        HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default,
        TimeSpan? timeout = default)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new EfCoreHealthCheck<TDbContext>(sp.GetRequiredService<IDbContextFactory<TDbContext>>()),
            failureStatus,
            tags ?? ["emit", "postgresql"],
            timeout));
    }
}

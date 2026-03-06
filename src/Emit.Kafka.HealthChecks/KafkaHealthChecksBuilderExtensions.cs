namespace Emit.Kafka.HealthChecks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Extension methods for registering the Emit Kafka health check.
/// </summary>
public static class KafkaHealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check that verifies connectivity to the Kafka broker cluster.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. Defaults to <c>emit-kafka</c>.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> reported when the check fails.
    /// Defaults to <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Optional tags for filtering health checks.</param>
    /// <param name="timeout">
    /// The metadata request timeout. Defaults to 5 seconds when not specified or set to
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddKafka()</c> to have been called on the Emit builder.
    /// The check issues a metadata request to the broker cluster without producing any messages.
    /// </remarks>
    public static IHealthChecksBuilder AddEmitKafka(
        this IHealthChecksBuilder builder,
        string name = "emit-kafka",
        HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default,
        TimeSpan? timeout = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new KafkaHealthCheck(sp.GetRequiredService<ConfluentKafka.IProducer<byte[], byte[]>>()),
            failureStatus,
            tags ?? ["emit", "kafka"],
            timeout));
    }
}

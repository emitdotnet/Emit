namespace Emit.Kafka.HealthChecks;

using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal sealed class KafkaHealthCheck(
    IProducer<byte[], byte[]> producer,
    Func<Handle, IAdminClient>? adminClientFactory = null) : IHealthCheck
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private static IAdminClient DefaultAdminClientFactory(Handle handle) =>
        new DependentAdminClientBuilder(handle).Build();

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var timeout = context.Registration.Timeout == System.Threading.Timeout.InfiniteTimeSpan
            ? DefaultTimeout
            : context.Registration.Timeout;

        try
        {
            // Share the producer's underlying rdkafka handle — no new connections or threads.
            using var adminClient = (adminClientFactory ?? DefaultAdminClientFactory)(producer.Handle);
            var metadata = adminClient.GetMetadata(timeout);
            return Task.FromResult(HealthCheckResult.Healthy($"{metadata.Brokers.Count} broker(s) reachable"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, exception: ex));
        }
    }
}

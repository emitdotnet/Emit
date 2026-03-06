namespace Emit.MongoDB.HealthChecks;

using Emit.MongoDB.Configuration;
using global::MongoDB.Bson;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal sealed class MongoDbHealthCheck(MongoDbContext mongoContext) : IHealthCheck
{
    private static readonly BsonDocument PingCommand = new("ping", 1);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await mongoContext.Database
                .RunCommandAsync<BsonDocument>(PingCommand, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}

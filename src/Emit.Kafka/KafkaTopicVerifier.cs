namespace Emit.Kafka;

using Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Hosted service that verifies all required Kafka topics exist before the application starts.
/// Registered at position 0 in the service collection so it runs before all other hosted services.
/// When auto-provisioning is enabled, creates missing topics; otherwise throws
/// <see cref="InvalidOperationException"/> and the host crashes.
/// </summary>
internal sealed class KafkaTopicVerifier(
    ConfluentKafka.IAdminClient adminClient,
    IReadOnlySet<string> requiredTopics,
    bool autoProvision,
    IReadOnlyDictionary<string, TopicCreationOptions> provisioningConfigs,
    ILogger<KafkaTopicVerifier> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (requiredTopics.Count == 0)
        {
            return;
        }

        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
        var existingTopics = new HashSet<string>(
            metadata.Topics
                .Where(t => t.Error.Code == ConfluentKafka.ErrorCode.NoError)
                .Select(t => t.Topic),
            StringComparer.Ordinal);

        var missing = requiredTopics.Where(t => !existingTopics.Contains(t)).ToList();
        if (missing.Count == 0)
        {
            logger.LogInformation("Verified {Count} Kafka topic(s) exist", requiredTopics.Count);
            return;
        }

        if (!autoProvision)
        {
            throw new InvalidOperationException(
                $"The following required Kafka topics do not exist: {string.Join(", ", missing)}. " +
                "Create them before starting the application, or call AutoProvision() to create them automatically.");
        }

        var specifications = missing
            .Select(topicName =>
            {
                var options = provisioningConfigs.GetValueOrDefault(topicName) ?? new TopicCreationOptions();
                return options.BuildSpecification(topicName);
            })
            .ToList();

        logger.LogInformation("Auto-provisioning {Count} missing Kafka topic(s): {Topics}",
            missing.Count, string.Join(", ", missing));

        await adminClient.CreateTopicsAsync(specifications).ConfigureAwait(false);

        logger.LogInformation("Successfully created {Count} Kafka topic(s)", missing.Count);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

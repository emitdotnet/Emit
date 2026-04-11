namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Consumer;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

[Trait("Category", "Integration")]
public class KafkaBatchConsumerCompliance(KafkaContainerFixture fixture)
    : BatchConsumerCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureEmitBatchConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchConfig> batchConfig)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddBatchConsumer<BatchSinkConsumer<string>>(batchConfig);
                });
            });
        });
    }

    protected override void ConfigureEmitBatchWithValidation(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId,
        Action<BatchConfig> batchConfig)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.DeadLetter(dlqTopic, t =>
            {
                t.ConsumerGroup(dlqGroupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DlqCaptureConsumer>();
                });
            });

            kafka.Topic<string, string>(sourceTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.Validate(
                        msg => msg.StartsWith("valid:", StringComparison.Ordinal)
                            ? Emit.Abstractions.MessageValidationResult.Success
                            : Emit.Abstractions.MessageValidationResult.Fail("invalid"),
                        a => a.DeadLetter());
                    group.OnError(e => e.Default(d => d.DeadLetter()));
                    group.AddBatchConsumer<BatchSinkConsumer<string>>(batchConfig);
                });
            });
        });
    }

    protected override void ConfigureEmitBatchWithRetry(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchConfig> batchConfig)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(e => e.Default(d => d.Retry(3, Backoff.None).Discard()));
                    group.AddBatchConsumer<FailNTimesBatchConsumer>(batchConfig);
                });
            });
        });
    }

    protected override void ConfigureEmitBatchWithRateLimit(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchConfig> batchConfig)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.WorkerCount = 1;
                    group.RateLimit(rl => rl.FixedWindow(2, TimeSpan.FromSeconds(2)));
                    group.AddBatchConsumer<BatchSinkConsumer<string>>(batchConfig);
                });
            });
        });
    }

    protected override void ConfigureEmitBatchWithCircuitBreaker(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchConfig> batchConfig)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.CircuitBreaker(cb =>
                    {
                        cb.FailureThreshold(2)
                          .SamplingWindow(TimeSpan.FromSeconds(30))
                          .PauseDuration(TimeSpan.FromSeconds(5))
                          .TripOn<InvalidOperationException>();
                    });
                    group.AddBatchConsumer<ToggleableBatchConsumer>(batchConfig);
                });
            });
        });
    }

    protected override void ConfigureEmitBatchWithValidationAndRetryDLQ(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string validationDlqTopic,
        string validationDlqGroupId,
        string handlerDlqTopic,
        string handlerDlqGroupId,
        Action<BatchConfig> batchConfig)
    {
        // Kafka supports a single DLQ destination per registration; both validation
        // failures and handler failures route to the same DLQ topic.
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.DeadLetter(handlerDlqTopic, t =>
            {
                t.ConsumerGroup(handlerDlqGroupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DlqCaptureConsumer>();
                });
            });

            kafka.Topic<string, string>(sourceTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.Validate(
                        msg => msg.StartsWith("valid:", StringComparison.Ordinal)
                            ? Emit.Abstractions.MessageValidationResult.Success
                            : Emit.Abstractions.MessageValidationResult.Fail("invalid"),
                        a => a.DeadLetter());
                    group.OnError(e => e.Default(d => d.Retry(1, Backoff.None).DeadLetter()));
                    group.AddBatchConsumer<AlwaysFailingBatchConsumer>(batchConfig);
                });
            });
        });
    }

    protected override void ConfigureEmitBatchWithDistributionStrategy(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchConfig> batchConfig,
        string distributionStrategy,
        int workerCount)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.WorkerCount = workerCount;
                    group.WorkerDistribution = distributionStrategy == nameof(WorkerDistribution.RoundRobin)
                        ? WorkerDistribution.RoundRobin
                        : WorkerDistribution.ByKeyHash;
                    group.AddBatchConsumer<BatchSinkConsumer<string>>(batchConfig);
                });
            });
        });
    }

    protected override void ConfigureEmitBatchContextInspector(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchConfig> batchConfig)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddBatchConsumer<BatchContextInspector>(batchConfig);
                });
            });
        });
    }
}

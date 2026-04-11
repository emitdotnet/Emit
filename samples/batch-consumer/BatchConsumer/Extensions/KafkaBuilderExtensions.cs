namespace BatchConsumer.Extensions;

using BatchConsumer.Consumers;
using BatchConsumer.Domain;
using Emit.Abstractions;
using Emit.FluentValidation;
using Emit.Kafka.DependencyInjection;

public static class KafkaBuilderExtensions
{
    /// <summary>
    /// Registers the <c>sorting.scans</c> and <c>sorting.reroutes</c> topics
    /// with their producer, batch consumer group, and middleware stack.
    /// </summary>
    public static KafkaBuilder AddPackageSorterTopics(this KafkaBuilder kafka)
    {
        kafka.AutoProvision();

        kafka.ConfigureProducer(producer =>
        {
            producer.Linger = TimeSpan.FromMilliseconds(50);
            producer.CompressionType = Confluent.Kafka.CompressionType.Lz4;
            producer.Acks = Confluent.Kafka.Acks.Leader;
        });

        kafka.DeadLetter("sorting.scans.dlt");

        kafka.Topic<string, PackageScan>("sorting.scans", topic =>
        {
            topic.SetUtf8KeySerializer();
            topic.SetJsonSchemaValueSerializer<string, PackageScan>();
            topic.SetUtf8KeyDeserializer();
            topic.SetJsonSchemaValueDeserializer<string, PackageScan>();

            topic.Provisioning(p => p.NumPartitions = 16);
            topic.Producer(p => p.UseDirect());

            topic.ConsumerGroup("sorting.sorter", group =>
            {
                group.WorkerCount = 4;
                group.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;

                group.AddBatchConsumer<PackageSortConsumer>(bc =>
                {
                    bc.MaxSize = 1000;
                    bc.Timeout = TimeSpan.FromSeconds(5);
                });

                group.ValidateWithFluentValidation(onFailure => onFailure.DeadLetter());

                group.OnError(e => e
                    .Default(a => a
                        .Retry(3, Backoff.Exponential(TimeSpan.FromMilliseconds(200)))
                        .DeadLetter()));

                group.OnDeserializationError(a => a.DeadLetter());

                group.CircuitBreaker(cb => cb
                    .FailureThreshold(5)
                    .SamplingWindow(TimeSpan.FromSeconds(30))
                    .PauseDuration(TimeSpan.FromSeconds(15)));
            });
        });

        kafka.Topic<string, RerouteCommand>("sorting.reroutes", topic =>
        {
            topic.SetUtf8KeySerializer();
            topic.SetJsonSchemaValueSerializer<string, RerouteCommand>();
            topic.Producer();
        });

        return kafka;
    }
}

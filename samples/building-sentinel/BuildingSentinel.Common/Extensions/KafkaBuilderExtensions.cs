namespace BuildingSentinel.Common.Extensions;

using BuildingSentinel.Common.Consumers;
using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Serialization;
using Emit.Abstractions;
using Emit.Kafka.DependencyInjection;
using Emit.Routing;

public static class KafkaBuilderExtensions
{
    /// <summary>
    /// Registers the <c>building.events</c> topic with its producer, the <c>building.classifier</c> router consumer group,
    /// and the <c>building.watchdog</c> consumer group.
    /// </summary>
    public static KafkaBuilder AddBuildingSentinelTopics(this KafkaBuilder kafka)
    {
        var serializer = new JsonKafkaSerializer<BuildingEvent>();

        kafka.Topic<string, BuildingEvent>("building.events", topic =>
        {
            topic.SetKeySerializer(Confluent.Kafka.Serializers.Utf8);
            topic.SetValueSerializer(serializer);
            topic.SetKeyDeserializer(Confluent.Kafka.Deserializers.Utf8);
            topic.SetValueDeserializer(serializer);

            topic.Producer();

            // Consumer group 1: routes on event type, only handles access.denied
            topic.ConsumerGroup("building.classifier", group =>
            {
                group.WorkerCount = 2;
                group.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;

                group.AddRouter<string>(
                    "event-type-router",
                    ctx => ctx.Message.EventType,
                    router => router.Route<AccessDeniedConsumer>("access.denied"));

                group.OnError(e => e
                    .WhenRouteUnmatched(a => a.Discard())
                    .Default(a => a.Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1))).Discard()));

                group.OnDeserializationError(a => a.Discard());

                group.CircuitBreaker(cb => cb
                    .FailureThreshold(5)
                    .SamplingWindow(TimeSpan.FromSeconds(30))
                    .PauseDuration(TimeSpan.FromSeconds(15)));
            });

            // Consumer group 2: handles ALL events — upserts device heartbeat
            topic.ConsumerGroup("building.watchdog", group =>
            {
                group.WorkerCount = 1;
                group.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                group.AddConsumer<DeviceHeartbeatConsumer>();

                group.OnError(e => e
                    .Default(a => a.Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1))).Discard()));

                group.OnDeserializationError(a => a.Discard());

                group.CircuitBreaker(cb => cb
                    .FailureThreshold(5)
                    .SamplingWindow(TimeSpan.FromSeconds(30))
                    .PauseDuration(TimeSpan.FromSeconds(15)));
            });
        });

        return kafka;
    }
}

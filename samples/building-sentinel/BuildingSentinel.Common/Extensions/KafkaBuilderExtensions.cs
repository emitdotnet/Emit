namespace BuildingSentinel.Common.Extensions;

using BuildingSentinel.Common.Consumers;
using BuildingSentinel.Common.Domain;
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
        kafka.AutoProvision();

        kafka.DeadLetter("building.events.dlt");

        kafka.Topic<string, BuildingEvent>("building.events", topic =>
        {
            topic.SetUtf8KeySerializer();
            topic.SetJsonSchemaValueSerializer<string, BuildingEvent>();
            topic.SetUtf8KeyDeserializer();
            topic.SetJsonSchemaValueDeserializer<string, BuildingEvent>();

            topic.Producer(p => p.UseOutbox());

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
                    .Default(a => a.Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1))).DeadLetter()));

                group.OnDeserializationError(a => a.DeadLetter());

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
                    .Default(a => a.Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1))).DeadLetter()));

                group.OnDeserializationError(a => a.DeadLetter());

                group.CircuitBreaker(cb => cb
                    .FailureThreshold(5)
                    .SamplingWindow(TimeSpan.FromSeconds(30))
                    .PauseDuration(TimeSpan.FromSeconds(15)));
            });
        });

        return kafka;
    }
}

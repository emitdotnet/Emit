namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Observability;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka implementation of <see cref="ConsumerFlowControlCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaConsumerFlowControlCompliance(KafkaContainerFixture fixture)
    : ConsumerFlowControlCompliance, IClassFixture<KafkaContainerFixture>
{
    /// <inheritdoc />
    protected override void ConfigureEmit(EmitBuilder emit, IReadOnlyList<FlowControlGroup> groups)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = fixture.BootstrapServers);
            kafka.AutoProvision();

            foreach (var topicGroups in groups.GroupBy(g => g.Topic))
            {
                kafka.Topic<string, string>(topicGroups.Key, t =>
                {
                    t.UseUtf8Serialization();
                    t.Provisioning(p => p.NumPartitions = topicGroups.First().Partitions);
                    t.Producer();
                    foreach (var declaration in topicGroups)
                    {
                        t.ConsumerGroup(declaration.GroupId, group =>
                        {
                            group.Name = declaration.Name;
                            group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                            if (declaration.CircuitBreaker is { } circuitBreaker)
                            {
                                group.CircuitBreaker(circuitBreaker);
                            }

                            switch (declaration.Consumer)
                            {
                                case GroupConsumer.Toggleable:
                                    group.AddConsumer<CountingToggleableConsumer>();
                                    break;
                                case GroupConsumer.Gated:
                                    group.AddConsumer<GatedConsumer>();
                                    break;
                                case GroupConsumer.Pausing:
                                    group.AddConsumer<PausingConsumer>();
                                    break;
                                case GroupConsumer.BatchSink:
                                    group.AddBatchConsumer<BatchSinkConsumer<string>>(b => b.Timeout = TimeSpan.FromMilliseconds(200));
                                    break;
                                default:
                                    group.AddConsumer<SinkConsumer<string>>();
                                    break;
                            }
                        });
                    }
                });
            }
        });
    }

    /// <inheritdoc />
    protected override void TrackAssignments(IServiceCollection services, AssignmentTracker assignments) =>
        services.AddSingleton<IKafkaConsumerObserver>(new AssignmentObserver(assignments));

    /// <inheritdoc />
    protected override void TrackCommits(IServiceCollection services, CommitTracker commits) =>
        services.AddSingleton<IKafkaConsumerObserver>(new CommitObserver(commits));

    private sealed class CommitObserver(CommitTracker commits) : IKafkaConsumerObserver
    {
        public Task OnOffsetsCommittedAsync(OffsetsCommittedEvent e)
        {
            foreach (var offset in e.Offsets)
            {
                commits.Committed(offset.Topic, offset.Offset);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class AssignmentObserver(AssignmentTracker assignments) : IKafkaConsumerObserver
    {
        public Task OnPartitionsAssignedAsync(PartitionsAssignedEvent e)
        {
            assignments.Assigned(e.Topic, e.Partitions);
            return Task.CompletedTask;
        }

        public Task OnPartitionsRevokedAsync(PartitionsRevokedEvent e)
        {
            assignments.Taken(e.Topic, e.Partitions);
            return Task.CompletedTask;
        }

        public Task OnPartitionsLostAsync(PartitionsLostEvent e)
        {
            assignments.Taken(e.Topic, e.Partitions);
            return Task.CompletedTask;
        }
    }
}

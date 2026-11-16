namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.DependencyInjection;
using global::Emit.Kafka.DependencyInjection;
using global::Emit.MongoDB.DependencyInjection;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class InboxRegistrationTests
{
    [Fact]
    public void GivenInboxConsumerNotTransactional_WhenAddingEmit_ThenThrows()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddEmit(emit =>
        {
            AddMongoDb(emit, useInbox: true);
            AddKafka(emit, group => group.AddConsumer<NonTransactionalConsumer>(c => c.UseInbox()));
        }));

        // Assert
        Assert.Contains(nameof(NonTransactionalConsumer), exception.Message);
        Assert.Contains(nameof(TransactionalAttribute), exception.Message);
    }

    [Fact]
    public void GivenInboxConsumerAndPersistenceWithoutInbox_WhenAddingEmit_ThenThrows()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddEmit(emit =>
        {
            AddKafka(emit, group => group.AddConsumer<TransactionalConsumer>(c => c.UseInbox()));
            AddMongoDb(emit, useInbox: false);
        }));

        // Assert
        Assert.Contains(nameof(TransactionalConsumer), exception.Message);
        Assert.Contains("UseInbox", exception.Message);
    }

    [Fact]
    public void GivenTwoInboxConsumersOfTheSameTypeNameInAGroup_WhenAddingEmit_ThenThrows()
    {
        // Arrange: the type is registered both directly and as a route
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddEmit(emit =>
        {
            AddMongoDb(emit, useInbox: true);
            AddKafka(emit, group =>
            {
                group.AddConsumer<TransactionalConsumer>(c => c.UseInbox());
                group.AddRouter("router", _ => "route", router => router
                    .Route<TransactionalConsumer>("route", c => c.UseInbox()));
            });
        }));

        // Assert
        Assert.Contains(nameof(TransactionalConsumer), exception.Message);
    }

    private static void AddMongoDb(EmitBuilder emit, bool useInbox)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = new MongoClient("mongodb://localhost:27017");
                ctx.Database = ctx.Client.GetDatabase("inbox-tests");
            });

            if (useInbox)
            {
                mongo.UseInbox();
            }
        });
    }

    private static void AddKafka(EmitBuilder emit, Action<KafkaConsumerGroupBuilder<string, string>> configureGroup)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
            kafka.Topic<string, string>("orders", topic =>
            {
                topic.SetUtf8KeyDeserializer();
                topic.SetUtf8ValueDeserializer();
                topic.ConsumerGroup("billing", configureGroup);
            });
        });
    }

    [Transactional]
    private sealed class TransactionalConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NonTransactionalConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

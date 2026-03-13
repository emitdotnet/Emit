namespace Emit.Kafka.Tests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.DependencyInjection;
using global::Emit.Kafka;
using global::Emit.Kafka.DependencyInjection;
using global::Emit.MongoDB.DependencyInjection;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaServiceCollectionExtensionsTests
{
    [Fact]
    public void GivenAddKafka_WhenConfigureClientNotCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka => { });
            }));
        Assert.Contains("ConfigureClient", exception.Message);
    }

    [Fact]
    public void GivenAddKafkaCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                });
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                });
            }));
    }

    [Fact]
    public void GivenNullBuilder_WhenAddKafka_ThenThrowsArgumentNullException()
    {
        // Arrange
        EmitBuilder builder = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            KafkaEmitBuilderExtensions.AddKafka(builder, _ => { }));
    }

    [Fact]
    public void GivenNullConfigure_WhenAddKafka_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddEmit(builder => builder.AddKafka(null!)));
    }

    [Fact]
    public void GivenOutboxEnabled_WhenAddKafka_ThenRegistersOutboxProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateMockMongoClient());
        services.AddSingleton(CreateMockMongoDatabase());

        // Act
        services.AddEmit(builder =>
        {
            builder.AddMongoDb(mongo =>
            {
                mongo.Configure((sp, ctx) =>
                {
                    ctx.Client = sp.GetRequiredService<global::MongoDB.Driver.IMongoClient>();
                    ctx.Database = sp.GetRequiredService<global::MongoDB.Driver.IMongoDatabase>();
                });
                mongo.UseOutbox();
            });
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
            });
        });

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IOutboxProvider));
    }

    [Fact]
    public void GivenOutboxDisabled_WhenAddKafka_ThenDoesNotRegisterOutboxProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
            });
        });

        // Assert
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IOutboxProvider));
    }

    [Fact]
    public void GivenAddKafka_WhenCalled_ThenReturnsEmitBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        EmitBuilder? capturedBuilder = null;
        EmitBuilder? result = null;

        // Act
        services.AddEmit(builder =>
        {
            capturedBuilder = builder;
            result = builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
            });
        });

        // Assert
        Assert.NotNull(capturedBuilder);
        Assert.Same(capturedBuilder, result);
    }

    [Fact]
    public void GivenTopicWithProducer_WhenOutboxEnabled_ThenRegistersKafkaOutboxProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateMockMongoClient());
        services.AddSingleton(CreateMockMongoDatabase());

        // Act
        services.AddEmit(builder =>
        {
            builder.AddMongoDb(mongo =>
            {
                mongo.Configure((sp, ctx) =>
                {
                    ctx.Client = sp.GetRequiredService<global::MongoDB.Driver.IMongoClient>();
                    ctx.Database = sp.GetRequiredService<global::MongoDB.Driver.IMongoDatabase>();
                });
                mongo.UseOutbox();
            });
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                kafka.Topic<string, string>("orders", t =>
                {
                    t.SetUtf8KeySerializer();
                    t.SetUtf8ValueSerializer();
                    t.Producer();
                });
            });
        });

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IOutboxProvider));
    }

    [Fact]
    public void GivenTopicWithProducer_WhenOutboxDisabled_ThenRegistersDirectEventProducer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                kafka.Topic<string, string>("orders", t =>
                {
                    t.SetUtf8KeySerializer();
                    t.SetUtf8ValueSerializer();
                    t.Producer();
                });
            });
        });

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IEventProducer<string, string>));
    }

    [Fact]
    public void GivenTopicWithConsumerGroup_WhenRegistered_ThenRegistersHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                kafka.Topic<string, string>("orders", t =>
                {
                    t.SetUtf8KeyDeserializer();
                    t.SetUtf8ValueDeserializer();
                    t.ConsumerGroup("order-processor", cg =>
                    {
                        cg.AddConsumer<TestConsumer>();
                    });
                });
            });
        });

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void GivenTopicWithConsumerGroup_WhenAddConsumerCalled_ThenRegistersConsumerAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                kafka.Topic<string, string>("orders", t =>
                {
                    t.SetUtf8KeyDeserializer();
                    t.SetUtf8ValueDeserializer();
                    t.ConsumerGroup("order-processor", cg =>
                    {
                        cg.AddConsumer<TestConsumer>();
                    });
                });
            });
        });

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(TestConsumer) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void GivenTopicWithProducer_WhenNoKeySerializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                    kafka.Topic<string, string>("orders", t =>
                    {
                        t.Producer();
                    });
                });
            }));
    }

    [Fact]
    public void GivenTopicWithProducer_WhenNoValueSerializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                    kafka.Topic<string, string>("orders", t =>
                    {
                        t.SetUtf8KeySerializer();
                        t.Producer();
                    });
                });
            }));
    }

    [Fact]
    public void GivenTopicWithProducer_WhenBothSyncAndAsyncKeySerializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                    kafka.Topic<string, string>("orders", t =>
                    {
                        t.SetUtf8KeySerializer();
                        t.SetKeySerializer(new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object);
                        t.SetUtf8ValueSerializer();
                        t.Producer();
                    });
                });
            }));
    }

    [Fact]
    public void GivenTopicWithConsumerGroup_WhenNoKeyDeserializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                    kafka.Topic<string, string>("orders", t =>
                    {
                        t.ConsumerGroup("order-processor", cg =>
                        {
                            cg.AddConsumer<TestConsumer>();
                        });
                    });
                });
            }));
    }

    [Fact]
    public void GivenTopicWithConsumerGroup_WhenNoValueDeserializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                    kafka.Topic<string, string>("orders", t =>
                    {
                        t.SetUtf8KeyDeserializer();
                        t.ConsumerGroup("order-processor", cg =>
                        {
                            cg.AddConsumer<TestConsumer>();
                        });
                    });
                });
            }));
    }

    [Fact]
    public void GivenTopicWithConsumerGroup_WhenBothSyncAndAsyncKeyDeserializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
                    kafka.Topic<string, string>("orders", t =>
                    {
                        t.SetUtf8KeyDeserializer();
                        t.SetKeyDeserializer(new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object);
                        t.SetUtf8ValueDeserializer();
                        t.ConsumerGroup("order-processor", cg =>
                        {
                            cg.AddConsumer<TestConsumer>();
                        });
                    });
                });
            }));
    }

    private static global::MongoDB.Driver.IMongoClient CreateMockMongoClient()
    {
        return new Mock<global::MongoDB.Driver.IMongoClient>().Object;
    }

    private static global::MongoDB.Driver.IMongoDatabase CreateMockMongoDatabase()
    {
        var mock = new Mock<global::MongoDB.Driver.IMongoDatabase> { DefaultValue = DefaultValue.Mock };
        var dbNamespace = new global::MongoDB.Driver.DatabaseNamespace("testdb");
        mock.Setup(x => x.DatabaseNamespace).Returns(dbNamespace);
        return mock.Object;
    }

    private sealed class TestConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

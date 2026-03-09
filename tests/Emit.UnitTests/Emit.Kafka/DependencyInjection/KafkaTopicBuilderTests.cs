namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Kafka.DependencyInjection;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaTopicBuilderTests
{
    private sealed class TestConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void GivenNewTopicBuilder_WhenProducer_ThenSetsProducerConfiguredTrue()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.Producer();

        // Assert
        Assert.True(builder.ProducerConfigured);
    }

    [Fact]
    public void GivenNewTopicBuilder_WhenProducerWithConfigAction_ThenStoresBuilder()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.Producer(_ => { });

        // Assert
        Assert.NotNull(builder.ProducerBuilder);
    }

    [Fact]
    public void GivenNewTopicBuilder_WhenProducerWithNullAction_ThenProducerBuilderIsNull()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.Producer();

        // Assert
        Assert.True(builder.ProducerConfigured);
        Assert.Null(builder.ProducerBuilder);
    }

    [Fact]
    public void GivenProducerAlreadyDeclared_WhenProducerCalledAgain_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.Producer();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Producer());
        Assert.Contains("test-topic", ex.Message);
    }

    [Fact]
    public void GivenNewTopicBuilder_WhenConsumerGroup_ThenAddsToConsumerGroups()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.ConsumerGroup("my-group", g => g.AddConsumer<TestConsumer>());

        // Assert
        Assert.Single(builder.ConsumerGroups);
        Assert.Equal("my-group", builder.ConsumerGroups[0].GroupId);
    }

    [Fact]
    public void GivenMultipleConsumerGroups_WhenConsumerGroup_ThenAllAdded()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.ConsumerGroup("group-1", g => g.AddConsumer<TestConsumer>());
        builder.ConsumerGroup("group-2", g => g.AddConsumer<TestConsumer>());
        builder.ConsumerGroup("group-3", g => g.AddConsumer<TestConsumer>());

        // Assert
        Assert.Equal(3, builder.ConsumerGroups.Count);
    }

    [Fact]
    public void GivenDuplicateGroupId_WhenConsumerGroup_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.ConsumerGroup("my-group", g => g.AddConsumer<TestConsumer>());

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.ConsumerGroup("my-group", g => g.AddConsumer<TestConsumer>()));
    }

    [Fact]
    public void GivenNullGroupId_WhenConsumerGroup_ThenThrowsArgumentException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => builder.ConsumerGroup(null!, g => g.AddConsumer<TestConsumer>()));
    }

    [Fact]
    public void GivenWhitespaceGroupId_WhenConsumerGroup_ThenThrowsArgumentException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => builder.ConsumerGroup("  ", g => g.AddConsumer<TestConsumer>()));
    }

    [Fact]
    public void GivenNullConfigureAction_WhenConsumerGroup_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConsumerGroup("my-group", null!));
    }

    [Fact]
    public void GivenConsumerGroup_WhenConfigureRuns_ThenReceivesConsumerGroupBuilder()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        KafkaConsumerGroupBuilder<string, string>? capturedBuilder = null;

        // Act
        builder.ConsumerGroup("my-group", g =>
        {
            capturedBuilder = g;
            g.AddConsumer<TestConsumer>();
        });

        // Assert
        Assert.NotNull(capturedBuilder);
    }

    [Fact]
    public void GivenGroupIdCaseDifference_WhenConsumerGroup_ThenBothSucceed()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.ConsumerGroup("my-group", g => g.AddConsumer<TestConsumer>());
        builder.ConsumerGroup("My-Group", g => g.AddConsumer<TestConsumer>());

        // Assert
        Assert.Equal(2, builder.ConsumerGroups.Count);
    }

    [Fact]
    public void GivenNewTopicBuilder_WhenNoProducerOrConsumerGroupDeclared_ThenBothStatesEmpty()
    {
        // Arrange & Act
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Assert
        Assert.False(builder.ProducerConfigured);
        Assert.Empty(builder.ConsumerGroups);
    }

    [Fact]
    public void GivenConsumerGroup_WhenNoAddConsumerCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.ConsumerGroup("group", g => { }));
    }

    // ── SetKeySerializer ──

    [Fact]
    public void GivenSetKeySerializer_WhenSync_ThenSetsKeySerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var serializer = new Mock<ConfluentKafka.ISerializer<string>>().Object;

        // Act
        builder.SetKeySerializer(serializer);

        // Assert
        Assert.Same(serializer, builder.KeySerializer);
        Assert.Null(builder.KeyAsyncSerializer);
    }

    [Fact]
    public void GivenSetKeySerializer_WhenAsync_ThenSetsKeyAsyncSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var serializer = new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object;

        // Act
        builder.SetKeySerializer(serializer);

        // Assert
        Assert.Same(serializer, builder.KeyAsyncSerializer);
        Assert.Null(builder.KeySerializer);
    }

    [Fact]
    public void GivenSetKeySerializer_WhenSyncAfterAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetKeySerializer(new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetKeySerializer(new Mock<ConfluentKafka.ISerializer<string>>().Object));
    }

    [Fact]
    public void GivenSetKeySerializer_WhenAsyncAfterSync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetKeySerializer(new Mock<ConfluentKafka.ISerializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetKeySerializer(new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object));
    }

    [Fact]
    public void GivenSetKeySerializer_WhenSyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetKeySerializer((ConfluentKafka.ISerializer<string>)null!));
    }

    [Fact]
    public void GivenSetKeySerializer_WhenAsyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetKeySerializer((ConfluentKafka.IAsyncSerializer<string>)null!));
    }

    // ── SetValueSerializer ──

    [Fact]
    public void GivenSetValueSerializer_WhenSync_ThenSetsValueSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var serializer = new Mock<ConfluentKafka.ISerializer<string>>().Object;

        // Act
        builder.SetValueSerializer(serializer);

        // Assert
        Assert.Same(serializer, builder.ValueSerializer);
        Assert.Null(builder.ValueAsyncSerializer);
    }

    [Fact]
    public void GivenSetValueSerializer_WhenAsync_ThenSetsValueAsyncSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var serializer = new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object;

        // Act
        builder.SetValueSerializer(serializer);

        // Assert
        Assert.Same(serializer, builder.ValueAsyncSerializer);
        Assert.Null(builder.ValueSerializer);
    }

    [Fact]
    public void GivenSetValueSerializer_WhenSyncAfterAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetValueSerializer(new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetValueSerializer(new Mock<ConfluentKafka.ISerializer<string>>().Object));
    }

    [Fact]
    public void GivenSetValueSerializer_WhenAsyncAfterSync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetValueSerializer(new Mock<ConfluentKafka.ISerializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetValueSerializer(new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object));
    }

    [Fact]
    public void GivenSetValueSerializer_WhenSyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetValueSerializer((ConfluentKafka.ISerializer<string>)null!));
    }

    [Fact]
    public void GivenSetValueSerializer_WhenAsyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetValueSerializer((ConfluentKafka.IAsyncSerializer<string>)null!));
    }

    // ── SetKeyDeserializer ──

    [Fact]
    public void GivenSetKeyDeserializer_WhenSync_ThenSetsKeyDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var deserializer = new Mock<ConfluentKafka.IDeserializer<string>>().Object;

        // Act
        builder.SetKeyDeserializer(deserializer);

        // Assert
        Assert.Same(deserializer, builder.KeyDeserializer);
        Assert.Null(builder.KeyAsyncDeserializer);
    }

    [Fact]
    public void GivenSetKeyDeserializer_WhenAsync_ThenSetsKeyAsyncDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var deserializer = new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object;

        // Act
        builder.SetKeyDeserializer(deserializer);

        // Assert
        Assert.Same(deserializer, builder.KeyAsyncDeserializer);
        Assert.Null(builder.KeyDeserializer);
    }

    [Fact]
    public void GivenSetKeyDeserializer_WhenSyncAfterAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetKeyDeserializer(new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetKeyDeserializer(new Mock<ConfluentKafka.IDeserializer<string>>().Object));
    }

    [Fact]
    public void GivenSetKeyDeserializer_WhenAsyncAfterSync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetKeyDeserializer(new Mock<ConfluentKafka.IDeserializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetKeyDeserializer(new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object));
    }

    [Fact]
    public void GivenSetKeyDeserializer_WhenSyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetKeyDeserializer((ConfluentKafka.IDeserializer<string>)null!));
    }

    [Fact]
    public void GivenSetKeyDeserializer_WhenAsyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetKeyDeserializer((ConfluentKafka.IAsyncDeserializer<string>)null!));
    }

    // ── SetValueDeserializer ──

    [Fact]
    public void GivenSetValueDeserializer_WhenSync_ThenSetsValueDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var deserializer = new Mock<ConfluentKafka.IDeserializer<string>>().Object;

        // Act
        builder.SetValueDeserializer(deserializer);

        // Assert
        Assert.Same(deserializer, builder.ValueDeserializer);
        Assert.Null(builder.ValueAsyncDeserializer);
    }

    [Fact]
    public void GivenSetValueDeserializer_WhenAsync_ThenSetsValueAsyncDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var deserializer = new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object;

        // Act
        builder.SetValueDeserializer(deserializer);

        // Assert
        Assert.Same(deserializer, builder.ValueAsyncDeserializer);
        Assert.Null(builder.ValueDeserializer);
    }

    [Fact]
    public void GivenSetValueDeserializer_WhenSyncAfterAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetValueDeserializer(new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetValueDeserializer(new Mock<ConfluentKafka.IDeserializer<string>>().Object));
    }

    [Fact]
    public void GivenSetValueDeserializer_WhenAsyncAfterSync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetValueDeserializer(new Mock<ConfluentKafka.IDeserializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetValueDeserializer(new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object));
    }

    [Fact]
    public void GivenSetValueDeserializer_WhenSyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetValueDeserializer((ConfluentKafka.IDeserializer<string>)null!));
    }

    [Fact]
    public void GivenSetValueDeserializer_WhenAsyncNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.SetValueDeserializer((ConfluentKafka.IAsyncDeserializer<string>)null!));
    }
}

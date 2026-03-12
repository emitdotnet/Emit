namespace Emit.Kafka.Tests;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using global::Emit.Kafka;
using global::Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class KafkaTopicVerifierTests
{
    private static Metadata BuildMetadata(params (string topic, ErrorCode errorCode)[] topics)
    {
        var brokers = new List<BrokerMetadata> { new BrokerMetadata(1, "localhost", 9092) };
        var topicMetadata = topics
            .Select(t => new TopicMetadata(t.topic, [], new Error(t.errorCode)))
            .ToList();
        return new Metadata(brokers, topicMetadata, 1, "localhost:9092");
    }

    private static KafkaTopicVerifier CreateVerifier(
        Mock<IAdminClient> adminClientMock,
        IReadOnlySet<string> requiredTopics,
        bool autoProvision = false,
        IReadOnlyDictionary<string, TopicCreationOptions>? provisioningConfigs = null)
    {
        return new KafkaTopicVerifier(
            adminClientMock.Object,
            requiredTopics,
            autoProvision,
            provisioningConfigs ?? new Dictionary<string, TopicCreationOptions>(),
            NullLogger<KafkaTopicVerifier>.Instance);
    }

    [Fact]
    public async Task GivenNoRequiredTopics_WhenStartAsync_ThenReturnsImmediately()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>(MockBehavior.Strict);
        var verifier = CreateVerifier(adminClientMock, new HashSet<string>());

        // Act
        await verifier.StartAsync(CancellationToken.None);

        // Assert
        adminClientMock.Verify(a => a.GetMetadata(It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task GivenAllTopicsExist_WhenStartAsync_ThenLogsSuccess()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>();
        var metadata = BuildMetadata(("topic-a", ErrorCode.NoError), ("topic-b", ErrorCode.NoError));
        adminClientMock.Setup(a => a.GetMetadata(It.IsAny<TimeSpan>())).Returns(metadata);

        var requiredTopics = new HashSet<string> { "topic-a", "topic-b" };
        var verifier = CreateVerifier(adminClientMock, requiredTopics);

        // Act
        await verifier.StartAsync(CancellationToken.None);

        // Assert
        adminClientMock.Verify(
            a => a.CreateTopicsAsync(It.IsAny<IEnumerable<TopicSpecification>>(), It.IsAny<CreateTopicsOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenMissingTopics_WhenAutoProvisionDisabled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>();
        var metadata = BuildMetadata(("topic-a", ErrorCode.NoError));
        adminClientMock.Setup(a => a.GetMetadata(It.IsAny<TimeSpan>())).Returns(metadata);

        var requiredTopics = new HashSet<string> { "topic-a", "topic-missing" };
        var verifier = CreateVerifier(adminClientMock, requiredTopics, autoProvision: false);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => verifier.StartAsync(CancellationToken.None));
        Assert.Contains("topic-missing", exception.Message);
    }

    [Fact]
    public async Task GivenMissingTopics_WhenAutoProvisionEnabled_ThenCreatesTopics()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>();
        var metadata = BuildMetadata();
        adminClientMock.Setup(a => a.GetMetadata(It.IsAny<TimeSpan>())).Returns(metadata);
        adminClientMock
            .Setup(a => a.CreateTopicsAsync(It.IsAny<IEnumerable<TopicSpecification>>(), It.IsAny<CreateTopicsOptions>()))
            .Returns(Task.CompletedTask);

        var requiredTopics = new HashSet<string> { "topic-new" };
        var verifier = CreateVerifier(adminClientMock, requiredTopics, autoProvision: true);

        // Act
        await verifier.StartAsync(CancellationToken.None);

        // Assert
        adminClientMock.Verify(
            a => a.CreateTopicsAsync(It.IsAny<IEnumerable<TopicSpecification>>(), It.IsAny<CreateTopicsOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenMissingTopics_WhenAutoProvisionEnabled_ThenOnlyCreatesMissingTopics()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>();
        var metadata = BuildMetadata(("topic-existing", ErrorCode.NoError));
        adminClientMock.Setup(a => a.GetMetadata(It.IsAny<TimeSpan>())).Returns(metadata);

        IEnumerable<TopicSpecification>? capturedSpecs = null;
        adminClientMock
            .Setup(a => a.CreateTopicsAsync(It.IsAny<IEnumerable<TopicSpecification>>(), It.IsAny<CreateTopicsOptions>()))
            .Callback<IEnumerable<TopicSpecification>, CreateTopicsOptions?>((specs, _) => capturedSpecs = specs)
            .Returns(Task.CompletedTask);

        var requiredTopics = new HashSet<string> { "topic-existing", "topic-missing" };
        var verifier = CreateVerifier(adminClientMock, requiredTopics, autoProvision: true);

        // Act
        await verifier.StartAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(capturedSpecs);
        var specList = capturedSpecs.ToList();
        Assert.Single(specList);
        Assert.Equal("topic-missing", specList[0].Name);
    }

    [Fact]
    public async Task GivenPerTopicProvisioning_WhenAutoProvisionEnabled_ThenUsesCorrectConfigPerTopic()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>();
        var metadata = BuildMetadata();
        adminClientMock.Setup(a => a.GetMetadata(It.IsAny<TimeSpan>())).Returns(metadata);

        IEnumerable<TopicSpecification>? capturedSpecs = null;
        adminClientMock
            .Setup(a => a.CreateTopicsAsync(It.IsAny<IEnumerable<TopicSpecification>>(), It.IsAny<CreateTopicsOptions>()))
            .Callback<IEnumerable<TopicSpecification>, CreateTopicsOptions?>((specs, _) => capturedSpecs = specs)
            .Returns(Task.CompletedTask);

        var requiredTopics = new HashSet<string> { "topic-custom" };
        var customOptions = new TopicCreationOptions
        {
            NumPartitions = 6,
            ReplicationFactor = 3,
        };
        var provisioningConfigs = new Dictionary<string, TopicCreationOptions>
        {
            ["topic-custom"] = customOptions,
        };
        var verifier = CreateVerifier(adminClientMock, requiredTopics, autoProvision: true, provisioningConfigs);

        // Act
        await verifier.StartAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(capturedSpecs);
        var specList = capturedSpecs.ToList();
        Assert.Single(specList);
        var spec = specList[0];
        Assert.Equal("topic-custom", spec.Name);
        Assert.Equal(6, spec.NumPartitions);
        Assert.Equal(3, spec.ReplicationFactor);
    }

    [Fact]
    public async Task GivenNoPerTopicProvisioning_WhenAutoProvisionEnabled_ThenUsesDefaults()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>();
        var metadata = BuildMetadata();
        adminClientMock.Setup(a => a.GetMetadata(It.IsAny<TimeSpan>())).Returns(metadata);

        IEnumerable<TopicSpecification>? capturedSpecs = null;
        adminClientMock
            .Setup(a => a.CreateTopicsAsync(It.IsAny<IEnumerable<TopicSpecification>>(), It.IsAny<CreateTopicsOptions>()))
            .Callback<IEnumerable<TopicSpecification>, CreateTopicsOptions?>((specs, _) => capturedSpecs = specs)
            .Returns(Task.CompletedTask);

        var requiredTopics = new HashSet<string> { "topic-default" };
        var verifier = CreateVerifier(adminClientMock, requiredTopics, autoProvision: true);

        // Act
        await verifier.StartAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(capturedSpecs);
        var specList = capturedSpecs.ToList();
        Assert.Single(specList);
        var spec = specList[0];
        Assert.Equal("topic-default", spec.Name);
        Assert.Equal(-1, spec.NumPartitions);
        Assert.Equal(-1, spec.ReplicationFactor);
        Assert.NotNull(spec.Configs);
        Assert.Equal("delete", spec.Configs["cleanup.policy"]);
        Assert.Equal("producer", spec.Configs["compression.type"]);
    }

    [Fact]
    public async Task GivenCreateTopicsAsyncThrows_WhenAutoProvisionEnabled_ThenExceptionPropagates()
    {
        // Arrange
        var adminClientMock = new Mock<IAdminClient>();
        var metadata = BuildMetadata();
        adminClientMock.Setup(a => a.GetMetadata(It.IsAny<TimeSpan>())).Returns(metadata);
        adminClientMock
            .Setup(a => a.CreateTopicsAsync(It.IsAny<IEnumerable<TopicSpecification>>(), It.IsAny<CreateTopicsOptions>()))
            .ThrowsAsync(new CreateTopicsException([]));

        var requiredTopics = new HashSet<string> { "topic-new" };
        var verifier = CreateVerifier(adminClientMock, requiredTopics, autoProvision: true);

        // Act & Assert
        await Assert.ThrowsAsync<CreateTopicsException>(
            () => verifier.StartAsync(CancellationToken.None));
    }
}

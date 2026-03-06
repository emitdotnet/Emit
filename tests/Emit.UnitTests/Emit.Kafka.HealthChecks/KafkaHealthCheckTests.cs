namespace Emit.Kafka.HealthChecks.Tests;

using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

public class KafkaHealthCheckTests
{
    private static HealthCheckContext CreateContext(
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        TimeSpan? timeout = null)
    {
        var registration = new HealthCheckRegistration(
            "test",
            _ => Mock.Of<IHealthCheck>(),
            failureStatus,
            [],
            timeout ?? System.Threading.Timeout.InfiniteTimeSpan);

        return new HealthCheckContext { Registration = registration };
    }

    [Fact]
    public async Task GivenBrokersReachable_WhenCheckHealth_ThenReturnsHealthy()
    {
        // Arrange
        var producerMock = new Mock<IProducer<byte[], byte[]>>();
        var adminClientMock = new Mock<IAdminClient>();

        adminClientMock
            .Setup(a => a.GetMetadata(It.IsAny<TimeSpan>()))
            .Returns(new Metadata(
                [new BrokerMetadata(1, "broker-1", 9092)],
                [],
                0,
                "cluster-id"));

        var check = new KafkaHealthCheck(producerMock.Object, _ => adminClientMock.Object);

        // Act
        var result = await check.CheckHealthAsync(CreateContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("1 broker(s) reachable", result.Description);
    }

    [Fact]
    public async Task GivenBrokerUnreachable_WhenCheckHealth_ThenReturnsUnhealthy()
    {
        // Arrange
        var producerMock = new Mock<IProducer<byte[], byte[]>>();
        var adminClientMock = new Mock<IAdminClient>();

        adminClientMock
            .Setup(a => a.GetMetadata(It.IsAny<TimeSpan>()))
            .Throws(new KafkaException(ErrorCode.Local_Transport));

        var check = new KafkaHealthCheck(producerMock.Object, _ => adminClientMock.Object);

        // Act
        var result = await check.CheckHealthAsync(CreateContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<KafkaException>(result.Exception);
    }

    [Fact]
    public async Task GivenCustomFailureStatus_WhenCheckHealth_ThenReturnsConfiguredStatus()
    {
        // Arrange
        var producerMock = new Mock<IProducer<byte[], byte[]>>();
        var adminClientMock = new Mock<IAdminClient>();

        adminClientMock
            .Setup(a => a.GetMetadata(It.IsAny<TimeSpan>()))
            .Throws(new KafkaException(ErrorCode.Local_Transport));

        var check = new KafkaHealthCheck(producerMock.Object, _ => adminClientMock.Object);

        // Act
        var result = await check.CheckHealthAsync(CreateContext(failureStatus: HealthStatus.Degraded));

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task GivenRegistrationTimeout_WhenCheckHealth_ThenTimeoutForwardedToGetMetadata()
    {
        // Arrange
        var producerMock = new Mock<IProducer<byte[], byte[]>>();
        var adminClientMock = new Mock<IAdminClient>();
        var capturedTimeout = TimeSpan.Zero;

        adminClientMock
            .Setup(a => a.GetMetadata(It.IsAny<TimeSpan>()))
            .Callback<TimeSpan>(t => capturedTimeout = t)
            .Returns(new Metadata(
                [new BrokerMetadata(1, "broker-1", 9092)],
                [],
                0,
                "cluster-id"));

        var registrationTimeout = TimeSpan.FromSeconds(10);
        var check = new KafkaHealthCheck(producerMock.Object, _ => adminClientMock.Object);

        // Act
        await check.CheckHealthAsync(CreateContext(timeout: registrationTimeout));

        // Assert
        Assert.Equal(registrationTimeout, capturedTimeout);
    }

    [Fact]
    public async Task GivenInfiniteTimeout_WhenCheckHealth_ThenDefaultTimeoutUsed()
    {
        // Arrange
        var producerMock = new Mock<IProducer<byte[], byte[]>>();
        var adminClientMock = new Mock<IAdminClient>();
        var capturedTimeout = TimeSpan.Zero;

        adminClientMock
            .Setup(a => a.GetMetadata(It.IsAny<TimeSpan>()))
            .Callback<TimeSpan>(t => capturedTimeout = t)
            .Returns(new Metadata(
                [new BrokerMetadata(1, "broker-1", 9092)],
                [],
                0,
                "cluster-id"));

        // Infinite timeout on registration → health check should use its own default (5s)
        var check = new KafkaHealthCheck(producerMock.Object, _ => adminClientMock.Object);

        // Act
        await check.CheckHealthAsync(CreateContext(timeout: System.Threading.Timeout.InfiniteTimeSpan));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), capturedTimeout);
    }
}

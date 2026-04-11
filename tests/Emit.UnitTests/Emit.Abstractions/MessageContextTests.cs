namespace Emit.Abstractions.Tests;

using global::Emit.Abstractions;
using Moq;
using Xunit;

public sealed class MessageContextTests
{
    private sealed class TestContext : MessageContext<string>
    {
    }

    private sealed class TestTransportContext : TransportContext
    {
    }

    private static ConsumeContext<string> CreateConsumeContext(
        IServiceProvider? services = null,
        string message = "hello")
    {
        services ??= new Mock<IServiceProvider>().Object;
        return new ConsumeContext<string>
        {
            Services = services,
            CancellationToken = CancellationToken.None,
            MessageId = "msg-id",
            Timestamp = DateTimeOffset.UtcNow,
            Message = message,
            TransportContext = new TestTransportContext
            {
                RawKey = null,
                RawValue = null,
                Headers = [],
                ProviderId = "test",
                MessageId = "msg-id",
                Timestamp = DateTimeOffset.UtcNow,
                CancellationToken = CancellationToken.None,
                Services = services,
            },
        };
    }

    [Fact]
    public void GivenMessageContext_WhenWithServicesCalled_ThenServicesReplaced()
    {
        // Arrange
        var originalServices = new Mock<IServiceProvider>().Object;
        var newServices = new Mock<IServiceProvider>().Object;
        var context = new TestContext
        {
            Services = originalServices,
            CancellationToken = CancellationToken.None,
            MessageId = "msg-id",
            Timestamp = DateTimeOffset.UtcNow,
            Message = "hello",
        };

        // Act
        context.WithServices(newServices);

        // Assert
        Assert.Same(newServices, context.Services);
    }

    [Fact]
    public void Given_ConsumeContext_When_WithMessageCalled_Then_MessageIsReplaced()
    {
        // Arrange
        var context = CreateConsumeContext(message: "original");

        // Act
        context.WithMessage("replaced");

        // Assert
        Assert.Equal("replaced", context.Message);
    }

    [Fact]
    public void Given_ConsumeContext_When_WithMessageCalled_Then_FeaturesPreserved()
    {
        // Arrange
        var context = CreateConsumeContext();
        var featureValue = new object();
        context.Features.Set(featureValue);

        // Act
        context.WithMessage("new-message");

        // Assert
        Assert.Same(featureValue, context.Features.Get<object>());
    }

    [Fact]
    public void Given_ConsumeContext_When_WithMessageCalled_Then_ServicesPreserved()
    {
        // Arrange
        var services = new Mock<IServiceProvider>().Object;
        var context = CreateConsumeContext(services: services);

        // Act
        context.WithMessage("new-message");

        // Assert
        Assert.Same(services, context.Services);
    }
}

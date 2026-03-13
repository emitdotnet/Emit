namespace Emit.Abstractions.Tests;

using global::Emit.Abstractions;
using Moq;
using Xunit;

public sealed class MessageContextTests
{
    private sealed class TestContext : MessageContext<string>
    {
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
}

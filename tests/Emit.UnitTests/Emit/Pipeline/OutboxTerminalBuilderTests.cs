namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Models;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

public sealed class OutboxTerminalBuilderTests
{
    private readonly Mock<IOutboxRepository> mockRepository = new();

    [Fact]
    public async Task GivenOutboxTerminal_WhenEnqueue_ThenEntryNodeIdMatchesINodeIdentity()
    {
        // Arrange
        var expectedNodeId = Guid.NewGuid();
        var mockNodeIdentity = new Mock<INodeIdentity>();
        mockNodeIdentity.Setup(n => n.NodeId).Returns(expectedNodeId);

        OutboxEntry? capturedEntry = null;
        mockRepository
            .Setup(r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEntry, CancellationToken>((entry, _) => capturedEntry = entry)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection()
            .AddSingleton(mockRepository.Object)
            .AddSingleton(mockNodeIdentity.Object)
            .BuildServiceProvider();

        var pipeline = OutboxTerminalBuilder.Build<string>((ctx, ct) => Task.FromResult(new OutboxEntry
        {
            SystemId = "test",
            Destination = "kafka://broker:9092/topic",
            GroupKey = "test-group"
        }));

        var context = new SendContext<string>
        {
            Message = "test-message",
            MessageId = "msg-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services
        };

        // Act
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.NotNull(capturedEntry);
        Assert.Equal(expectedNodeId, capturedEntry.NodeId);
    }

    [Fact]
    public async Task GivenNullTransaction_WhenOutboxTerminalInvoked_ThenDoesNotThrow()
    {
        // Arrange
        mockRepository
            .Setup(r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection()
            .AddSingleton(mockRepository.Object)
            .AddSingleton(new Mock<INodeIdentity>().Object)
            .BuildServiceProvider();

        var pipeline = OutboxTerminalBuilder.Build<string>((ctx, ct) => Task.FromResult(new OutboxEntry
        {
            SystemId = "test",
            Destination = "kafka://broker:9092/topic",
            GroupKey = "test-group"
        }));

        var context = new SendContext<string>
        {
            Message = "test-message",
            MessageId = "msg-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services
        };

        // Act
        await pipeline.InvokeAsync(context);

        // Assert
        mockRepository.Verify(
            r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

namespace Emit.Abstractions.Tests.Pipeline;

using Emit;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

public class InboundContextTests
{
    [Fact]
    public void GivenInboundContext_WhenGettingTransaction_ThenDelegatesToEmitContext()
    {
        // Arrange
        var services = new ServiceCollection();
        var emitContext = new EmitContext();
        var mockTransaction = new Mock<ITransactionContext>();
        emitContext.Transaction = mockTransaction.Object;

        services.AddSingleton<IEmitContext>(emitContext);
        var serviceProvider = services.BuildServiceProvider();

        var context = new TestInboundContext
        {
            Services = serviceProvider,
            Message = "test",
            MessageId = "msg-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None
        };

        // Act
        var transaction = context.Transaction;

        // Assert
        Assert.Same(mockTransaction.Object, transaction);
    }

    [Fact]
    public void GivenInboundContext_WhenSettingTransaction_ThenDelegatesToEmitContext()
    {
        // Arrange
        var services = new ServiceCollection();
        var emitContext = new EmitContext();
        services.AddSingleton<IEmitContext>(emitContext);
        var serviceProvider = services.BuildServiceProvider();

        var context = new TestInboundContext
        {
            Services = serviceProvider,
            Message = "test",
            MessageId = "msg-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None
        };

        var mockTransaction = new Mock<ITransactionContext>();

        // Act
        context.Transaction = mockTransaction.Object;

        // Assert
        Assert.Same(mockTransaction.Object, context.Transaction);
        Assert.Same(mockTransaction.Object, emitContext.Transaction);
    }

    [Fact]
    public void GivenInboundContext_WhenAccessedMultipleTimes_ThenSameEmitContextResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        var emitContext = new EmitContext();
        services.AddSingleton<IEmitContext>(emitContext);
        var serviceProvider = services.BuildServiceProvider();

        var context = new TestInboundContext
        {
            Services = serviceProvider,
            Message = "test",
            MessageId = "msg-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None
        };

        var mockTransaction1 = new Mock<ITransactionContext>();
        var mockTransaction2 = new Mock<ITransactionContext>();

        // Act - Access transaction multiple times
        context.Transaction = mockTransaction1.Object;
        var firstAccess = context.Transaction;
        context.Transaction = null; // Clear
        context.Transaction = mockTransaction2.Object;
        var secondAccess = context.Transaction;

        // Assert - Both accesses should hit the same EmitContext instance
        Assert.Same(mockTransaction2.Object, emitContext.Transaction);
        Assert.Same(emitContext.Transaction, secondAccess);
    }

    private class TestInboundContext : InboundContext<string>
    {
    }
}

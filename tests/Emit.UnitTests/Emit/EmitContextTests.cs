namespace Emit.UnitTests;

using Emit;
using Emit.Abstractions;
using Moq;
using Xunit;

public class EmitContextTests
{
    [Fact]
    public void GivenNoTransaction_WhenSettingTransaction_ThenTransactionIsSet()
    {
        // Arrange
        var context = new EmitContext();
        var mockTransaction = new Mock<ITransactionContext>();

        // Act
        context.Transaction = mockTransaction.Object;

        // Assert
        Assert.Same(mockTransaction.Object, context.Transaction);
    }

    [Fact]
    public void GivenActiveTransaction_WhenSettingSameReference_ThenNoException()
    {
        // Arrange
        var context = new EmitContext();
        var mockTransaction = new Mock<ITransactionContext>();
        context.Transaction = mockTransaction.Object;

        // Act & Assert (should not throw)
        context.Transaction = mockTransaction.Object;
        Assert.Same(mockTransaction.Object, context.Transaction);
    }

    [Fact]
    public void GivenActiveTransaction_WhenSettingDifferentTransaction_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var context = new EmitContext();
        var firstTransaction = new Mock<ITransactionContext>();
        var secondTransaction = new Mock<ITransactionContext>();
        context.Transaction = firstTransaction.Object;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.Transaction = secondTransaction.Object);

        Assert.Contains("Cannot overwrite an active transaction", exception.Message);
    }

    [Fact]
    public void GivenActiveTransaction_WhenSettingNull_ThenTransactionIsCleared()
    {
        // Arrange
        var context = new EmitContext();
        var mockTransaction = new Mock<ITransactionContext>();
        context.Transaction = mockTransaction.Object;

        // Act
        context.Transaction = null;

        // Assert
        Assert.Null(context.Transaction);
    }

    [Fact]
    public void GivenNullTransaction_WhenSettingNull_ThenNoException()
    {
        // Arrange
        var context = new EmitContext();

        // Act & Assert (should not throw)
        context.Transaction = null;
        Assert.Null(context.Transaction);
    }
}

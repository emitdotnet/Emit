namespace Emit.MongoDB.Tests;

using global::MongoDB.Driver;
using Moq;
using Xunit;

public class MongoSessionHolderTests
{
    [Fact]
    public void GivenNewHolder_WhenSessionAccessed_ThenReturnsNull()
    {
        // Arrange
        var holder = new MongoSessionHolder();

        // Act
        var session = holder.Session;

        // Assert
        Assert.Null(session);
    }

    [Fact]
    public void GivenHolder_WhenSessionSet_ThenSessionReturnsValue()
    {
        // Arrange
        var holder = new MongoSessionHolder();
        var mockSession = new Mock<IClientSessionHandle>();

        // Act
        holder.Session = mockSession.Object;

        // Assert
        Assert.Same(mockSession.Object, holder.Session);
    }
}

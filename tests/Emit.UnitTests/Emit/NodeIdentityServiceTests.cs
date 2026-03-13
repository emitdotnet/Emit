namespace Emit.UnitTests;

using Xunit;

public sealed class NodeIdentityServiceTests
{
    [Fact]
    public void GivenNewInstance_WhenNodeId_ThenReturnsNonEmptyGuid()
    {
        // Arrange
        var service = new NodeIdentityService();

        // Act
        var nodeId = service.NodeId;

        // Assert
        Assert.NotEqual(Guid.Empty, nodeId);
    }

    [Fact]
    public void GivenInstance_WhenNodeIdCalledTwice_ThenReturnsSameValue()
    {
        // Arrange
        var service = new NodeIdentityService();

        // Act
        var first = service.NodeId;
        var second = service.NodeId;

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void GivenTwoInstances_WhenNodeId_ThenReturnsDifferentValues()
    {
        // Arrange
        var serviceA = new NodeIdentityService();
        var serviceB = new NodeIdentityService();

        // Act
        var idA = serviceA.NodeId;
        var idB = serviceB.NodeId;

        // Assert
        Assert.NotEqual(idA, idB);
    }
}

namespace Emit.Abstractions.Tests;

using Xunit;

public sealed class EventMessageTests
{
    [Fact]
    public void GivenKeyAndHeaders_WhenTombstoneCreated_ThenItCarriesTheKeyAndHeadersAndNoValue()
    {
        // Arrange
        List<KeyValuePair<string, string>> headers = [new("reason", "deleted")];

        // Act
        var tombstone = EventMessage<string, string>.Tombstone("order-1", headers);

        // Assert
        Assert.Equal("order-1", tombstone.Key);
        Assert.Null(tombstone.Value);
        Assert.Equal(headers, tombstone.Headers);
    }

    [Fact]
    public void GivenOnlyAKey_WhenTombstoneCreated_ThenItHasNoHeaders()
    {
        // Act
        var tombstone = EventMessage<string, string>.Tombstone("order-1");

        // Assert
        Assert.Empty(tombstone.Headers);
    }
}

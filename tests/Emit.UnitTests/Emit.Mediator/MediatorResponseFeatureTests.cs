namespace Emit.Mediator.Tests;

using global::Emit.Mediator;
using Xunit;

public sealed class MediatorResponseFeatureTests
{
    [Fact]
    public void GivenNewFeature_WhenCheckHasResponded_ThenReturnsFalse()
    {
        // Arrange
        var feature = new MediatorResponseFeature();

        // Act & Assert
        Assert.False(feature.HasResponded);
    }

    [Fact]
    public async Task GivenResponse_WhenRespondAsync_ThenHasRespondedIsTrue()
    {
        // Arrange
        var feature = new MediatorResponseFeature();

        // Act
        await feature.RespondAsync("hello");

        // Assert
        Assert.True(feature.HasResponded);
    }

    [Fact]
    public async Task GivenResponse_WhenGetResponse_ThenReturnsTypedValue()
    {
        // Arrange
        var feature = new MediatorResponseFeature();
        await feature.RespondAsync("hello");

        // Act
        var result = feature.GetResponse<string>();

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GivenAlreadyResponded_WhenRespondAsyncAgain_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var feature = new MediatorResponseFeature();
        await feature.RespondAsync("first");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => feature.RespondAsync("second"));
    }

    [Fact]
    public void GivenNoResponse_WhenGetResponse_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var feature = new MediatorResponseFeature();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => feature.GetResponse<string>());
    }
}

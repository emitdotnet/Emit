namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using Xunit;

public sealed class FeatureCollectionTests
{
    [Fact]
    public void GivenNoFeature_WhenGet_ThenReturnsNull()
    {
        // Arrange
        var features = new FeatureCollection();

        // Act
        var result = features.Get<IResponseFeature>();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenSetFeature_WhenGet_ThenReturnsSameInstance()
    {
        // Arrange
        var features = new FeatureCollection();
        var expected = new TestResponseFeature();
        features.Set<IResponseFeature>(expected);

        // Act
        var result = features.Get<IResponseFeature>();

        // Assert
        Assert.Same(expected, result);
    }

    [Fact]
    public void GivenExistingFeature_WhenSetAgain_ThenOverwrites()
    {
        // Arrange
        var features = new FeatureCollection();
        var first = new TestResponseFeature();
        var second = new TestResponseFeature();
        features.Set<IResponseFeature>(first);

        // Act
        features.Set<IResponseFeature>(second);

        // Assert
        Assert.Same(second, features.Get<IResponseFeature>());
    }

    [Fact]
    public void GivenDifferentFeatureTypes_WhenSetAndGet_ThenEachReturnsCorrect()
    {
        // Arrange
        var features = new FeatureCollection();
        var response = new TestResponseFeature();
        var headers = new TestHeadersFeature();
        features.Set<IResponseFeature>(response);
        features.Set<IHeadersFeature>(headers);

        // Act & Assert
        Assert.Same(response, features.Get<IResponseFeature>());
        Assert.Same(headers, features.Get<IHeadersFeature>());
    }

    [Fact]
    public void GivenNullFeature_WhenSet_ThenThrowsArgumentNullException()
    {
        // Arrange
        var features = new FeatureCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => features.Set<IResponseFeature>(null!));
    }

    private sealed class TestResponseFeature : IResponseFeature
    {
        public bool HasResponded => false;
        public Task RespondAsync<TResponse>(TResponse response) => Task.CompletedTask;
    }

    private sealed class TestHeadersFeature : IHeadersFeature
    {
        public IReadOnlyList<KeyValuePair<string, string>> Headers => [];
    }
}

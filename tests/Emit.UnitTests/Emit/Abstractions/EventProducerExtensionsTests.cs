namespace Emit.UnitTests.Abstractions;

using global::Emit.Abstractions;
using Moq;
using Xunit;

public class EventProducerExtensionsTests
{
    private readonly Mock<IEventProducer<string, string>> _producerMock = new();

    [Fact]
    public async Task GivenKeyAndValue_WhenProduceAsync_ThenDelegatesWithEventMessage()
    {
        // Arrange
        EventMessage<string, string>? captured = null;
        _producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<EventMessage<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<EventMessage<string, string>, CancellationToken>((msg, _) => captured = msg)
            .Returns(Task.CompletedTask);

        // Act
        await _producerMock.Object.ProduceAsync("key", "value");

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("key", captured.Key);
        Assert.Equal("value", captured.Value);
        Assert.Empty(captured.Headers);
    }

    [Fact]
    public async Task GivenKeyValueAndHeaders_WhenProduceAsync_ThenDelegatesWithEventMessage()
    {
        // Arrange
        EventMessage<string, string>? captured = null;
        IReadOnlyList<KeyValuePair<string, string>> headers = [new("correlation-id", "abc")];
        _producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<EventMessage<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<EventMessage<string, string>, CancellationToken>((msg, _) => captured = msg)
            .Returns(Task.CompletedTask);

        // Act
        await _producerMock.Object.ProduceAsync("key", "value", headers);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("key", captured.Key);
        Assert.Equal("value", captured.Value);
        Assert.Equal(headers, captured.Headers);
    }

    [Fact]
    public async Task GivenCancellationToken_WhenProduceAsync_ThenPassesTokenThrough()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;
        _producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<EventMessage<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<EventMessage<string, string>, CancellationToken>((_, ct) => capturedToken = ct)
            .Returns(Task.CompletedTask);

        // Act
        await _producerMock.Object.ProduceAsync("key", "value", cts.Token);

        // Assert
        Assert.Equal(cts.Token, capturedToken);
    }
}

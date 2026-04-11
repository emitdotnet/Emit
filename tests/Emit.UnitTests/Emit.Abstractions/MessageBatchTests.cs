namespace Emit.Abstractions.Tests;

using global::Emit.Abstractions;
using Xunit;

public sealed class MessageBatchTests
{
    private sealed class ConcreteTransportContext : TransportContext
    {
    }

    private static ConcreteTransportContext MakeTransport() => new()
    {
        RawKey = null,
        RawValue = null,
        Headers = [],
        ProviderId = "test",
        MessageId = "tc-1",
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Services = null!,
    };

    private static BatchItem<string> MakeItem(string message) => new()
    {
        Message = message,
        TransportContext = MakeTransport(),
    };

    [Fact]
    public void Given_Items_When_Constructed_Then_CountMatchesItemCount()
    {
        // Arrange
        var items = new List<BatchItem<string>>
        {
            MakeItem("a"),
            MakeItem("b"),
            MakeItem("c"),
        };

        // Act
        var batch = new MessageBatch<string>(items);

        // Assert
        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public void Given_Items_When_Constructed_Then_IndexerReturnsCorrectItem()
    {
        // Arrange
        var item0 = MakeItem("first");
        var item1 = MakeItem("second");
        var items = new List<BatchItem<string>> { item0, item1 };

        // Act
        var batch = new MessageBatch<string>(items);

        // Assert
        Assert.Same(item0, batch[0]);
        Assert.Same(item1, batch[1]);
    }

    [Fact]
    public void Given_Items_When_GetItemTransportContextsCalled_Then_YieldsEachItemTransportContext()
    {
        // Arrange
        var tc0 = MakeTransport();
        var tc1 = MakeTransport();
        var items = new List<BatchItem<string>>
        {
            new() { Message = "a", TransportContext = tc0 },
            new() { Message = "b", TransportContext = tc1 },
        };
        var batch = new MessageBatch<string>(items);

        // Act
        var contexts = ((IBatchMessage)batch).GetItemTransportContexts().ToList();

        // Assert
        Assert.Equal(2, contexts.Count);
        Assert.Same(tc0, contexts[0]);
        Assert.Same(tc1, contexts[1]);
    }

    [Fact]
    public void Given_NullItems_When_Constructed_Then_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MessageBatch<string>(null!));
    }

    [Fact]
    public void Given_Items_When_Enumerated_Then_YieldsAllItemsInOrder()
    {
        // Arrange
        var item0 = MakeItem("x");
        var item1 = MakeItem("y");
        var item2 = MakeItem("z");
        var items = new List<BatchItem<string>> { item0, item1, item2 };
        var batch = new MessageBatch<string>(items);

        // Act
        var enumerated = batch.ToList();

        // Assert
        Assert.Equal(3, enumerated.Count);
        Assert.Same(item0, enumerated[0]);
        Assert.Same(item1, enumerated[1]);
        Assert.Same(item2, enumerated[2]);
    }

    [Fact]
    public void Given_Items_When_ItemsPropertyAccessed_Then_ReturnsSameList()
    {
        // Arrange
        var items = new List<BatchItem<string>> { MakeItem("a") };
        var batch = new MessageBatch<string>(items);

        // Act
        var result = batch.Items;

        // Assert
        Assert.Same(items, result);
    }
}

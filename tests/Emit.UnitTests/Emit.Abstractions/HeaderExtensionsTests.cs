namespace Emit.Abstractions.Tests;

using global::Emit.Abstractions;
using Xunit;

public sealed class HeaderExtensionsTests
{
    private readonly List<KeyValuePair<string, string>> headers =
    [
        new("TraceParent", "first"),
        new("other", "value"),
        new("traceparent", "last"),
    ];

    [Fact]
    public void GivenAKeyRepeatedInAnotherCase_WhenGettingTheFirstAndLastValue_ThenBothCasesMatch()
    {
        // Act
        var first = headers.GetFirstValue(WellKnownHeaders.TraceParent);
        var last = headers.GetLastValue(WellKnownHeaders.TraceParent);

        // Assert
        Assert.Equal("first", first);
        Assert.Equal("last", last);
    }

    [Fact]
    public void GivenAKeyRepeatedInAnotherCase_WhenUpserted_ThenItAppearsOnceWithTheNewValue()
    {
        // Act
        headers.Upsert(WellKnownHeaders.TraceParent, "new");

        // Assert
        Assert.Equal([new("other", "value"), new(WellKnownHeaders.TraceParent, "new")], headers);
    }
}

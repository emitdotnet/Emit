namespace Emit.MongoDB.Tests.Configuration;

using global::Emit.MongoDB.Configuration;
using Xunit;

public class BsonConfigurationTests
{
    [Fact]
    public void GivenMultipleCalls_WhenConfigure_ThenDoesNotThrow()
    {
        // Arrange & Act - Call Configure multiple times
        // This tests idempotency: the method should be safe to call multiple times
        // without throwing exceptions or corrupting global state

        // Act
        var exception = Record.Exception(() =>
        {
            BsonConfiguration.Configure();
            BsonConfiguration.Configure();
            BsonConfiguration.Configure();
        });

        // Assert
        Assert.Null(exception);
    }
}

namespace Emit.Abstractions.Tests;

using global::Emit.Abstractions;
using Xunit;

public sealed class EmitEndpointAddressTests
{
    [Theory]
    [InlineData("kafka", "broker", 9092, "orders", "kafka://broker:9092/orders")]
    [InlineData("kafka", "broker", null, "orders", "kafka://broker/orders")]
    [InlineData("kafka", "broker", 9092, null, "kafka://broker:9092/")]
    public void GivenAddress_WhenWrittenAsUriAndParsedBack_ThenEveryPartIsKept(
        string scheme,
        string host,
        int? port,
        string? entityName,
        string expectedUri)
    {
        // Arrange
        var address = entityName is null
            ? EmitEndpointAddress.ForHost(scheme, host, port)
            : EmitEndpointAddress.ForEntity(scheme, host, port, entityName);

        // Act
        var uri = address.ToUri();
        var parsed = EmitEndpointAddress.FromUri(uri);

        // Assert
        Assert.Equal(expectedUri, uri.ToString());
        Assert.Equal(address, parsed);
        Assert.Equal(entityName, EmitEndpointAddress.GetEntityName(uri));
    }
}

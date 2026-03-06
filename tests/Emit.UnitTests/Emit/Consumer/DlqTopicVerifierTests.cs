namespace Emit.UnitTests.Consumer;

using global::Emit.Consumer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class DlqTopicVerifierTests
{
    private readonly ILogger logger = NullLogger.Instance;

    [Fact]
    public void GivenAllTopicsExist_WhenVerified_ThenNoException()
    {
        // Arrange
        var required = new HashSet<string> { "orders.dlt", "payments.dlt" };
        var existing = new HashSet<string> { "orders", "orders.dlt", "payments", "payments.dlt" };

        // Act & Assert — should not throw
        DlqTopicVerifier.Verify(required, () => existing, logger);
    }

    [Fact]
    public void GivenOneTopicMissing_WhenVerified_ThenThrowsWithTopicName()
    {
        // Arrange
        var required = new HashSet<string> { "orders.dlt", "payments.dlt" };
        var existing = new HashSet<string> { "orders", "orders.dlt", "payments" };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => DlqTopicVerifier.Verify(required, () => existing, logger));
        Assert.Contains("payments.dlt", ex.Message);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void GivenEmptyRequiredTopics_WhenVerified_ThenSkipsVerification()
    {
        // Arrange
        var required = new HashSet<string>();
        var fetchCalled = false;

        // Act
        DlqTopicVerifier.Verify(
            required,
            () => { fetchCalled = true; return new HashSet<string>(); },
            logger);

        // Assert — getExistingTopics should never be called
        Assert.False(fetchCalled);
    }

    [Fact]
    public void GivenMetadataFetchThrows_WhenVerified_ThenExceptionPropagates()
    {
        // Arrange
        var required = new HashSet<string> { "orders.dlt" };

        // Act & Assert
        Assert.Throws<TimeoutException>(
            () => DlqTopicVerifier.Verify(
                required,
                () => throw new TimeoutException("broker unavailable"),
                logger));
    }
}

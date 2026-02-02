namespace Emit.Tests.Abstractions;

using Emit.Abstractions;
using Xunit;

public class LeaseAcquisitionResultTests
{
    [Fact]
    public void GivenAcquiredLease_WhenCreatingResult_ThenPropertiesAreSet()
    {
        // Arrange
        var leaseUntil = DateTime.UtcNow.AddMinutes(1);

        // Act
        var result = new LeaseAcquisitionResult(true, leaseUntil);

        // Assert
        Assert.True(result.Acquired);
        Assert.Equal(leaseUntil, result.LeaseUntil);
        Assert.Null(result.CurrentHolderId);
    }

    [Fact]
    public void GivenFailedAcquisition_WhenCreatingResult_ThenPropertiesIncludeCurrentHolder()
    {
        // Arrange
        var leaseUntil = DateTime.UtcNow.AddMinutes(1);
        var currentHolder = "worker-2";

        // Act
        var result = new LeaseAcquisitionResult(false, leaseUntil, currentHolder);

        // Assert
        Assert.False(result.Acquired);
        Assert.Equal(leaseUntil, result.LeaseUntil);
        Assert.Equal(currentHolder, result.CurrentHolderId);
    }

    [Fact]
    public void GivenLeaseResult_WhenUsingWith_ThenCreatesModifiedCopy()
    {
        // Arrange
        var original = new LeaseAcquisitionResult(false, DateTime.UtcNow, "worker-1");

        // Act
        var modified = original with { Acquired = true, CurrentHolderId = null };

        // Assert
        Assert.True(modified.Acquired);
        Assert.Equal(original.LeaseUntil, modified.LeaseUntil);
        Assert.Null(modified.CurrentHolderId);
    }
}

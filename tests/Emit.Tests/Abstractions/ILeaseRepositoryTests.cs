namespace Emit.Tests.Abstractions;

using Emit.Abstractions;
using Moq;
using Xunit;

public class ILeaseRepositoryTests
{
    private readonly Mock<ILeaseRepository> mockRepository;

    public ILeaseRepositoryTests()
    {
        mockRepository = new Mock<ILeaseRepository>();
    }

    [Fact]
    public async Task GivenWorkerId_WhenTryAcquireOrRenewLeaseAsync_ThenReturnsResult()
    {
        // Arrange
        var workerId = "worker-1";
        var leaseDuration = TimeSpan.FromMinutes(1);
        var leaseUntil = DateTime.UtcNow.Add(leaseDuration);
        var expectedResult = new LeaseAcquisitionResult(true, leaseUntil);

        mockRepository
            .Setup(r => r.TryAcquireOrRenewLeaseAsync(workerId, leaseDuration, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await mockRepository.Object.TryAcquireOrRenewLeaseAsync(workerId, leaseDuration);

        // Assert
        Assert.True(result.Acquired);
        Assert.Equal(leaseUntil, result.LeaseUntil);
    }

    [Fact]
    public async Task GivenLeaseHeldByAnother_WhenTryAcquireOrRenewLeaseAsync_ThenReturnsNotAcquired()
    {
        // Arrange
        var workerId = "worker-1";
        var leaseDuration = TimeSpan.FromMinutes(1);
        var leaseUntil = DateTime.UtcNow.AddSeconds(45);
        var expectedResult = new LeaseAcquisitionResult(false, leaseUntil, "worker-2");

        mockRepository
            .Setup(r => r.TryAcquireOrRenewLeaseAsync(workerId, leaseDuration, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await mockRepository.Object.TryAcquireOrRenewLeaseAsync(workerId, leaseDuration);

        // Assert
        Assert.False(result.Acquired);
        Assert.Equal("worker-2", result.CurrentHolderId);
        Assert.Equal(leaseUntil, result.LeaseUntil);
    }

    [Fact]
    public async Task GivenLeaseHolder_WhenReleaseLeaseAsync_ThenReturnsTrue()
    {
        // Arrange
        var workerId = "worker-1";
        mockRepository
            .Setup(r => r.ReleaseLeaseAsync(workerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await mockRepository.Object.ReleaseLeaseAsync(workerId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GivenNonHolder_WhenReleaseLeaseAsync_ThenReturnsFalse()
    {
        // Arrange
        var workerId = "worker-1";
        mockRepository
            .Setup(r => r.ReleaseLeaseAsync(workerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await mockRepository.Object.ReleaseLeaseAsync(workerId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GivenRepository_WhenEnsureLeaseExistsAsync_ThenCompletes()
    {
        // Arrange
        mockRepository
            .Setup(r => r.EnsureLeaseExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act & Assert (no exception means success)
        await mockRepository.Object.EnsureLeaseExistsAsync();

        mockRepository.Verify(r => r.EnsureLeaseExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

namespace Emit.EntityFrameworkCore.HealthChecks.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

public class EfCoreHealthCheckTests
{
    private static HealthCheckContext CreateHealthCheckContext(HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "test",
                _ => Mock.Of<IHealthCheck>(),
                failureStatus,
                [])
        };

    [Fact]
    public async Task GivenCanConnect_WhenCheckHealth_ThenReturnsHealthy()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: nameof(GivenCanConnect_WhenCheckHealth_ThenReturnsHealthy))
            .Options;
        var factory = new TestDbContextFactory(options);
        var check = new EfCoreHealthCheck<TestDbContext>(factory);

        // Act
        var result = await check.CheckHealthAsync(CreateHealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task GivenFactoryThrows_WhenCheckHealth_ThenReturnsUnhealthy()
    {
        // Arrange
        var factory = new ThrowingDbContextFactory(new InvalidOperationException("Factory failed"));
        var check = new EfCoreHealthCheck<TestDbContext>(factory);

        // Act
        var result = await check.CheckHealthAsync(CreateHealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task GivenCustomFailureStatus_WhenCheckHealth_ThenReturnsConfiguredStatus()
    {
        // Arrange
        var factory = new ThrowingDbContextFactory(new InvalidOperationException("Factory failed"));
        var check = new EfCoreHealthCheck<TestDbContext>(factory);

        // Act
        var result = await check.CheckHealthAsync(CreateHealthCheckContext(failureStatus: HealthStatus.Degraded));

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    // --- Test helpers ---

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    private sealed class TestDbContextFactory(DbContextOptions<TestDbContext> options)
        : IDbContextFactory<TestDbContext>
    {
        public TestDbContext CreateDbContext() => new(options);
    }

    private sealed class ThrowingDbContextFactory(Exception exception) : IDbContextFactory<TestDbContext>
    {
        public TestDbContext CreateDbContext() => throw exception;
    }
}

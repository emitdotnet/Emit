namespace Emit.UnitTests.Routing;

using Emit.Abstractions;
using Emit.Routing;
using Xunit;

public sealed class MessageRouterBuilderTests
{
    [Fact]
    public void GivenSingleRoute_WhenBuilding_ThenReturnsRegistration()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();
        builder.Route<ConsumerA>("key-a");

        // Act
        var registration = builder.Build(ctx => ctx.Message);

        // Assert
        Assert.Single(registration.ConsumerTypes);
        Assert.Equal(typeof(ConsumerA), registration.ConsumerTypes[0]);
    }

    [Fact]
    public void GivenMultipleRoutes_WhenBuilding_ThenRegistrationContainsAll()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();
        builder.Route<ConsumerA>("key-a");
        builder.Route<ConsumerB>("key-b");

        // Act
        var registration = builder.Build(ctx => ctx.Message);

        // Assert
        Assert.Equal(2, registration.ConsumerTypes.Count);
        Assert.Contains(typeof(ConsumerA), registration.ConsumerTypes);
        Assert.Contains(typeof(ConsumerB), registration.ConsumerTypes);
    }

    [Fact]
    public void GivenDuplicateRouteKey_WhenAddingRoute_ThenThrowsInvalidOperation()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();
        builder.Route<ConsumerA>("key-a");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Route<ConsumerB>("key-a"));
        Assert.Contains("key-a", ex.Message);
    }

    [Fact]
    public void GivenDuplicateConsumerType_WhenAddingRoute_ThenThrowsInvalidOperation()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();
        builder.Route<ConsumerA>("key-a");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Route<ConsumerA>("key-b"));
        Assert.Contains(nameof(ConsumerA), ex.Message);
    }

    [Fact]
    public void GivenNoRoutes_WhenBuilding_ThenThrowsInvalidOperation()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.Build(ctx => ctx.Message));
    }

    [Fact]
    public void GivenEnumRouteKey_WhenAddingRoutes_ThenDuplicateKeyThrows()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, EventType>();
        builder.Route<ConsumerA>(EventType.Created);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.Route<ConsumerB>(EventType.Created));
    }

    [Fact]
    public void GivenRouteWithPerRouteMiddleware_WhenBuilding_ThenRegistrationContainsRoute()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();
        var configureCalled = false;

        builder.Route<ConsumerA>("key-a", route =>
        {
            configureCalled = true;
        });

        // Act
        var registration = builder.Build(ctx => ctx.Message);

        // Assert
        Assert.True(configureCalled);
        Assert.Single(registration.ConsumerTypes);
    }

    [Fact]
    public void GivenNullSelector_WhenBuilding_ThenThrowsArgumentNull()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();
        builder.Route<ConsumerA>("key-a");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.Build(null!));
    }

    [Fact]
    public void GivenNullConfigure_WhenAddingRouteWithConfig_ThenThrowsArgumentNull()
    {
        // Arrange
        var builder = new MessageRouterBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.Route<ConsumerA>("key-a", null!));
    }

    // ── Test helpers ──

    private sealed class ConsumerA : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ConsumerB : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private enum EventType
    {
        Created,
        Updated,
    }
}

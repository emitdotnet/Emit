namespace Emit.UnitTests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.DependencyInjection;
using global::Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void GivenNullServices_WhenAddEmit_ThenThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services!.AddEmit(_ => { }));
    }

    [Fact]
    public void GivenNullConfigure_WhenAddEmit_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddEmit(null!));
    }

    [Fact]
    public void GivenValidConfiguration_WhenAddEmit_ThenReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(_ => { });
            });
        });

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void GivenValidConfiguration_WhenAddEmit_ThenIEmitContextResolvesToEmitContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(_ => { });
            });
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var emitContext = scope.ServiceProvider.GetRequiredService<EmitContext>();
        var iEmitContext = scope.ServiceProvider.GetRequiredService<IEmitContext>();

        // Assert
        Assert.IsType<EmitContext>(iEmitContext);
        Assert.Same(emitContext, iEmitContext);
    }

    [Fact]
    public void GivenAddEmit_WhenResolvingINodeIdentity_ThenReturnsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(_ => { });
            });
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<INodeIdentity>();
        var second = provider.GetRequiredService<INodeIdentity>();

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public void GivenAddEmit_WhenResolvingEmitMetricsEnrichment_ThenContainsNodeIdTag()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(builder =>
        {
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(_ => { });
            });
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var enrichment = provider.GetRequiredService<EmitMetricsEnrichment>();

        // Assert
        var tagKeys = enrichment.Tags.ToArray().Select(t => t.Key).ToList();
        Assert.Contains("emit.node.id", tagKeys);
    }

}

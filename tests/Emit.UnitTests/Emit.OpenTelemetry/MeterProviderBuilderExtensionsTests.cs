namespace Emit.OpenTelemetry.Tests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.DependencyInjection;
using global::OpenTelemetry;
using global::OpenTelemetry.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MeterProviderBuilderExtensionsTests
{
    [Fact]
    public void GivenAddEmitInstrumentation_WhenResolvingTheEnrichment_ThenItCarriesTheNodeIdServiceNameAndCustomTags()
    {
        // Arrange: the instrumentation replaces the enrichment AddEmit registers, so it must keep the core tags
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.ConfigureLeaderElection(options => options.ServiceName = "product-sync"));
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddEmitInstrumentation(options => options.EnrichWithTag("env", "prod")));
        using var provider = services.BuildServiceProvider();

        // Act
        var tags = provider.GetRequiredService<EmitMetricsEnrichment>().Tags.ToArray().ToDictionary(t => t.Key, t => t.Value);

        // Assert
        Assert.Equal(provider.GetRequiredService<INodeIdentity>().NodeId.ToString(), tags["emit.node.id"]);
        Assert.Equal("product-sync", tags["emit.service.name"]);
        Assert.Equal("prod", tags["env"]);
    }

    [Fact]
    public void GivenDefaultOptions_WhenAddEmitInstrumentation_ThenNoExceptionIsThrown()
    {
        // Arrange & Act
        using var meterProvider = global::OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .AddEmitInstrumentation()
            .Build();

        // Assert
        Assert.NotNull(meterProvider);
    }

    [Fact]
    public void GivenDisabledMeters_WhenAddEmitInstrumentation_ThenNoExceptionIsThrown()
    {
        // Arrange & Act
        using var meterProvider = global::OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .AddEmitInstrumentation(opts =>
            {
                opts.EnableEmitMeter = false;
                opts.EnableOutboxMeter = false;
                opts.EnableLockMeter = false;
                opts.EnableMediatorMeter = false;
                opts.EnableKafkaMeter = true;
                opts.EnableKafkaBrokerMeter = true;
            })
            .Build();

        // Assert
        Assert.NotNull(meterProvider);
    }

    [Fact]
    public void GivenNullBuilder_WhenAddEmitInstrumentation_ThenThrowsArgumentNullException()
    {
        // Arrange
        MeterProviderBuilder builder = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddEmitInstrumentation());
    }

    [Fact]
    public void GivenAllMetersDisabled_WhenAddEmitInstrumentation_ThenNoExceptionIsThrown()
    {
        // Arrange & Act
        using var meterProvider = global::OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .AddEmitInstrumentation(opts =>
            {
                opts.EnableEmitMeter = false;
                opts.EnableOutboxMeter = false;
                opts.EnableLockMeter = false;
                opts.EnableMediatorMeter = false;
                opts.EnableKafkaMeter = false;
                opts.EnableKafkaBrokerMeter = false;
            })
            .Build();

        // Assert
        Assert.NotNull(meterProvider);
    }

    [Fact]
    public void GivenEmitInstrumentationOptions_WhenEnrichWithTag_ThenTagsAreCollected()
    {
        // Arrange
        var options = new EmitInstrumentationOptions();

        // Act
        options.EnrichWithTag("env", "test");
        options.EnrichWithTag("service", "my-app");

        // Assert
        var tags = options.GetTags();
        Assert.Equal(2, tags.Length);
        Assert.Contains(tags.ToArray(), t => t.Key == "env" && Equals(t.Value, "test"));
        Assert.Contains(tags.ToArray(), t => t.Key == "service" && Equals(t.Value, "my-app"));
    }

    [Fact]
    public void GivenEmitInstrumentationOptions_WhenEnrichWithNullKey_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new EmitInstrumentationOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => options.EnrichWithTag(null!, "value"));
    }

    [Fact]
    public void GivenEmitInstrumentationOptions_WhenEnrichWithEmptyKey_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new EmitInstrumentationOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.EnrichWithTag("", "value"));
    }

    [Fact]
    public void GivenDefaultEmitInstrumentationOptions_WhenChecked_ThenAllMetersAreEnabled()
    {
        // Arrange
        var options = new EmitInstrumentationOptions();

        // Assert
        Assert.True(options.EnableEmitMeter);
        Assert.True(options.EnableOutboxMeter);
        Assert.True(options.EnableLockMeter);
        Assert.True(options.EnableMediatorMeter);
        Assert.True(options.EnableKafkaMeter);
        Assert.True(options.EnableKafkaBrokerMeter);
    }

    [Fact]
    public void GivenNoTags_WhenGetTags_ThenReturnsEmptyMemory()
    {
        // Arrange
        var options = new EmitInstrumentationOptions();

        // Act
        var tags = options.GetTags();

        // Assert
        Assert.Equal(0, tags.Length);
    }
}

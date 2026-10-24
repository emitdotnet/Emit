namespace Emit.OpenTelemetry.Tests;

using System.Diagnostics.Metrics;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.DependencyInjection;
using global::Emit.Metrics;
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
    public void GivenPackageMetersRegistered_WhenOneIsDisabled_ThenOnlyTheOthersAreSubscribed()
    {
        // Arrange: two meters a package registers, recorded on before the metrics are collected
        var subscribed = $"test.package.{Guid.NewGuid():N}";
        var disabled = $"test.package.{Guid.NewGuid():N}";
        var exported = new CapturingExporter();
        using var meterProvider = global::OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .ConfigureServices(services => services
                .AddSingleton(new PackageMeter(subscribed))
                .AddSingleton(new PackageMeter(disabled)))
            .AddEmitInstrumentation(options => options.DisableMeter(disabled))
            .AddReader(new BaseExportingMetricReader(exported))
            .Build();
        using var subscribedMeter = new Meter(subscribed);
        using var disabledMeter = new Meter(disabled);

        // Act
        subscribedMeter.CreateCounter<long>("subscribed.count").Add(1);
        disabledMeter.CreateCounter<long>("disabled.count").Add(1);
        meterProvider.ForceFlush();

        // Assert
        Assert.Contains("subscribed.count", exported.Names);
        Assert.DoesNotContain("disabled.count", exported.Names);
    }

    [Fact]
    public void GivenEmitDurationHistogram_WhenExported_ThenItUsesTheAdvisedBoundaries()
    {
        // Arrange: no view configures Emit's histograms, they advise their own boundaries
        var exported = new CapturingExporter();
        using var meterProvider = global::OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .AddEmitInstrumentation()
            .AddReader(new BaseExportingMetricReader(exported))
            .Build();
        var metrics = new EmitMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.RecordRateLimitWaitDuration(0.3);
        meterProvider.ForceFlush();

        // Assert
        Assert.Equal(HistogramAdvice.Seconds.HistogramBucketBoundaries, exported.Boundaries["emit.consumer.rate_limit.wait_duration"]);
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

    /// <summary>
    /// Captures the names of the exported metrics and the bucket boundaries of the histograms among them.
    /// </summary>
    private sealed class CapturingExporter : BaseExporter<Metric>
    {
        public HashSet<string> Names { get; } = [];

        public Dictionary<string, List<double>> Boundaries { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                Names.Add(metric.Name);
                if (metric.MetricType != MetricType.Histogram)
                {
                    continue;
                }

                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var boundaries = new List<double>();
                    foreach (var bucket in point.GetHistogramBuckets())
                    {
                        if (!double.IsPositiveInfinity(bucket.ExplicitBound))
                        {
                            boundaries.Add(bucket.ExplicitBound);
                        }
                    }

                    Boundaries[metric.Name] = boundaries;
                }
            }

            return ExportResult.Success;
        }
    }
}

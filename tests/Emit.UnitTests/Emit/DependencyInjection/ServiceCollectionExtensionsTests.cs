namespace Emit.UnitTests.DependencyInjection;

using System.Reflection;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Daemon;
using global::Emit.Abstractions.LeaderElection;
using global::Emit.Abstractions.Metrics;
using global::Emit.Daemon;
using global::Emit.DependencyInjection;
using global::Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
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

    [Theory]
    [InlineData("", 1)]
    [InlineData("product-sync", 3_600)]
    public void GivenPersistenceAndUnsafeCoordinationOptions_WhenTheHostStarts_ThenItRefusesToStart(string serviceName, int drainSeconds)
    {
        // Arrange: a node without a service name, or one whose daemons may outlast the time the leader waits
        // before moving them, must never run; the validators only protect a host they run in
        var services = new ServiceCollection();
        services.AddEmit(builder =>
        {
            builder.Services.AddSingleton(new PersistenceProviderMarker("test"));
            builder.ConfigureLeaderElection(options => options.ServiceName = serviceName);
            builder.ConfigureDaemons(options => options.DrainTimeout = TimeSpan.FromSeconds(drainSeconds));
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var failure = Record.Exception(() => provider.GetRequiredService<IStartupValidator>().Validate());

        // Assert
        Assert.IsType<OptionsValidationException>(failure);
    }

    [Theory]
    [InlineData("Product.Worker", null, "Product.Worker")]
    [InlineData("Product.Worker", "product-sync", "product-sync")]
    [InlineData(null, null, null)]
    [InlineData(null, "product-sync", "product-sync")]
    public void GivenHostEnvironmentAndExplicitServiceName_WhenResolvingINodeIdentity_ThenTheExplicitNameWinsOverTheApplicationNameAndThenTheEntryAssembly(
        string? applicationName,
        string? explicitServiceName,
        string? expected)
    {
        // Arrange: a null application name means no host environment, and a null expectation the entry assembly's name
        var services = new ServiceCollection();
        if (applicationName is not null)
        {
            services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.ApplicationName == applicationName));
        }

        services.AddEmit(builder =>
        {
            if (explicitServiceName is not null)
            {
                builder.ConfigureLeaderElection(options => options.ServiceName = explicitServiceName);
            }

            builder.AddKafka(kafka => kafka.ConfigureClient(_ => { }));
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var serviceName = provider.GetRequiredService<INodeIdentity>().ServiceName;

        // Assert
        Assert.Equal(expected ?? Assembly.GetEntryAssembly()!.GetName().Name, serviceName);
    }

    [Fact]
    public void GivenAddEmit_WhenResolvingEmitMetricsEnrichment_ThenContainsTheNodeIdAndServiceNameTags()
    {
        // Arrange: services sharing a database can run daemons with the same identifier, so their metrics are told
        // apart by service
        var services = new ServiceCollection();
        services.AddEmit(builder =>
        {
            builder.ConfigureLeaderElection(options => options.ServiceName = "product-sync");
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(_ => { });
            });
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var enrichment = provider.GetRequiredService<EmitMetricsEnrichment>();

        // Assert
        var tags = enrichment.Tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(provider.GetRequiredService<INodeIdentity>().NodeId.ToString(), tags["emit.node.id"]);
        Assert.Equal("product-sync", tags["emit.service.name"]);
    }

    [Fact]
    public void GivenDaemonsAddedByTypeAndByFactory_WhenTheCoordinatorIsCreated_ThenEachIsOneSingletonDaemon()
    {
        // Arrange: one class can back several daemons through factories, and an instance typed as the interface is
        // registered once, not once as itself and once as a daemon
        var services = new ServiceCollection();
        IDaemonAgent typedAsInterface = new NamedDaemon("typed-as-interface");
        services.AddEmit(builder =>
        {
            builder.Services.AddSingleton(new PersistenceProviderMarker("test"));
            builder.Services.AddSingleton(Mock.Of<IDaemonAssignmentPersistence>());
            builder.Services.AddSingleton(Mock.Of<ILeaderElectionPersistence>());
            builder.AddDaemon<NightlyReportDaemon>();
            builder.AddDaemon(_ => new NamedDaemon("first"));
            builder.AddDaemon(_ => new NamedDaemon("second"));
            builder.AddDaemon(_ => typedAsInterface);
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var coordinator = Record.Exception(() => provider.GetRequiredService<DaemonCoordinator>());
        var daemons = provider.GetServices<IDaemonAgent>().ToList();

        // Assert
        Assert.Null(coordinator);
        Assert.Equal(
            ["first", NightlyReportDaemon.Id, "second", "typed-as-interface"],
            daemons.Select(d => d.DaemonId).Order(StringComparer.Ordinal));
        Assert.Same(provider.GetRequiredService<NightlyReportDaemon>(), daemons.OfType<NightlyReportDaemon>().Single());
        Assert.Equal(daemons, provider.GetServices<IDaemonAgent>());
    }

    [Theory]
    [InlineData(DaemonRegistration.ByType)]
    [InlineData(DaemonRegistration.ByFactory)]
    [InlineData(DaemonRegistration.DirectlyOnTheServices)]
    public void GivenDaemonWithoutPersistenceProvider_WhenAddingEmit_ThenItFailsNamingTheDaemon(DaemonRegistration registration)
    {
        // Arrange: without a persistence provider there is no coordination, so the daemon would never run
        var services = new ServiceCollection();

        // Act
        var failure = Record.Exception(() => services.AddEmit(builder =>
        {
            switch (registration)
            {
                case DaemonRegistration.ByType:
                    builder.AddDaemon<NightlyReportDaemon>();
                    break;
                case DaemonRegistration.ByFactory:
                    builder.AddDaemon(_ => new NightlyReportDaemon());
                    break;
                default:
                    builder.Services.AddSingleton<IDaemonAgent, NightlyReportDaemon>();
                    break;
            }
        }));

        // Assert
        Assert.Contains(nameof(NightlyReportDaemon), Assert.IsType<InvalidOperationException>(failure).Message);
    }

    public enum DaemonRegistration
    {
        ByType,
        ByFactory,
        DirectlyOnTheServices,
    }

    private sealed class NightlyReportDaemon : IDaemonAgent
    {
        public const string Id = "nightly-report";

        public string DaemonId => Id;

        public Task RunAsync(DaemonRunContext context) => Task.Delay(Timeout.InfiniteTimeSpan, context.AssignmentToken);
    }

    private sealed class NamedDaemon(string daemonId) : IDaemonAgent
    {
        public string DaemonId { get; } = daemonId;

        public Task RunAsync(DaemonRunContext context) => Task.Delay(Timeout.InfiniteTimeSpan, context.AssignmentToken);
    }
}

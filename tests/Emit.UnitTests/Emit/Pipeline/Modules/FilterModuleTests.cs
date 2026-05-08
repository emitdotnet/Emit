namespace Emit.UnitTests.Pipeline.Modules;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class FilterModuleTests
{
    // ── Predicate registration ──

    [Fact]
    public async Task GivenMultiplePredicates_WhenRegistered_ThenPreservesOrder()
    {
        // Arrange
        var module = new FilterModule<string>();
        var callOrder = new List<int>();

        module.AddPredicate((_, _) => { callOrder.Add(1); return ValueTask.FromResult(true); });
        module.AddPredicate((_, _) => { callOrder.Add(2); return ValueTask.FromResult(true); });
        module.AddPredicate((_, _) => { callOrder.Add(3); return ValueTask.FromResult(true); });

        // Act
        var context = BuildContext("msg", new ServiceCollection().BuildServiceProvider());
        await module.EvaluateAsync(context, CancellationToken.None);

        // Assert — all three entries were evaluated in registration order
        Assert.Equal([1, 2, 3], callOrder);
    }

    [Fact]
    public async Task GivenPredicateReturningFalse_WhenEvaluated_ThenReturnsFalse()
    {
        // Arrange
        var module = new FilterModule<string>();
        module.AddPredicate((_, _) => ValueTask.FromResult(false));

        // Act
        var context = BuildContext("msg", new ServiceCollection().BuildServiceProvider());
        var result = await module.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GivenAllPredicatesReturningTrue_WhenEvaluated_ThenReturnsTrue()
    {
        // Arrange
        var module = new FilterModule<string>();
        module.AddPredicate((_, _) => ValueTask.FromResult(true));
        module.AddPredicate((_, _) => ValueTask.FromResult(true));

        // Act
        var context = BuildContext("msg", new ServiceCollection().BuildServiceProvider());
        var result = await module.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    // ── Class-based filter registration ──

    [Fact]
    public void GivenFilterTypeRegistration_WhenRegisterServicesCalled_ThenTypeRegisteredAsTransient()
    {
        // Arrange
        var module = new FilterModule<string>();
        module.AddFilterType<AlwaysPassFilter>();
        var services = new ServiceCollection();

        // Act
        module.RegisterServices(services);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(AlwaysPassFilter));
    }

    [Fact]
    public async Task GivenClassBasedFilter_WhenEvaluated_ThenResolvedFromServices()
    {
        // Arrange
        var module = new FilterModule<string>();
        module.AddFilterType<AlwaysPassFilter>();

        var services = new ServiceCollection();
        services.AddTransient<AlwaysPassFilter>();
        var provider = services.BuildServiceProvider();
        var context = BuildContext("msg", provider);

        // Act
        var result = await module.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    // ── Mixed registration ──

    [Fact]
    public async Task GivenMixedPredicateAndTypeEntries_WhenEvaluated_ThenBothAreEvaluated()
    {
        // Arrange
        var predicateCalled = false;
        var module = new FilterModule<string>();
        module.AddPredicate((_, _) =>
        {
            predicateCalled = true;
            return ValueTask.FromResult(true);
        });
        module.AddFilterType<AlwaysPassFilter>();

        var services = new ServiceCollection();
        services.AddTransient<AlwaysPassFilter>();
        var provider = services.BuildServiceProvider();
        var context = BuildContext("msg", provider);

        // Act
        var result = await module.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.True(predicateCalled);
    }

    [Fact]
    public void GivenNoEntries_WhenHasEntriesChecked_ThenReturnsFalse()
    {
        // Arrange
        var module = new FilterModule<string>();

        // Assert
        Assert.False(module.HasEntries);
    }

    [Fact]
    public void GivenEntryAdded_WhenHasEntriesChecked_ThenReturnsTrue()
    {
        // Arrange
        var module = new FilterModule<string>();
        module.AddPredicate((_, _) => ValueTask.FromResult(true));

        // Assert
        Assert.True(module.HasEntries);
    }

    // ── Helpers ──

    private static ConsumeContext<string> BuildContext(string message, IServiceProvider services)
    {
        return new TestConsumeContext<string>
        {
            Message = message,
            MessageId = "test",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            TransportContext = new TestTransportContext
            {
                RawKey = null,
                RawValue = null,
                Headers = [],
                ProviderId = "test",
                MessageId = "tc",
                Timestamp = DateTimeOffset.UtcNow,
                CancellationToken = CancellationToken.None,
                Services = services,
            },
        };
    }

    private sealed class TestConsumeContext<T> : ConsumeContext<T>;

    private sealed class TestTransportContext : TransportContext;

    private sealed class AlwaysPassFilter : IConsumerFilter<string>
    {
        public ValueTask<bool> ShouldConsumeAsync(ConsumeContext<string> context, CancellationToken ct)
            => ValueTask.FromResult(true);
    }
}

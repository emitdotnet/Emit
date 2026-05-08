namespace Emit.UnitTests.Pipeline.Modules;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class FilterMiddlewareTests
{
    // ── Predicate registration ──

    [Fact]
    public async Task GivenMultiplePredicates_WhenInvoked_ThenEvaluatedInRegistrationOrder()
    {
        // Arrange
        var module = new FilterMiddleware<string>();
        var callOrder = new List<int>();

        module.AddPredicate((_, _) => { callOrder.Add(1); return ValueTask.FromResult(true); });
        module.AddPredicate((_, _) => { callOrder.Add(2); return ValueTask.FromResult(true); });
        module.AddPredicate((_, _) => { callOrder.Add(3); return ValueTask.FromResult(true); });

        // Act
        var context = BuildContext("msg", new ServiceCollection().BuildServiceProvider());
        var nextCalled = false;
        await module.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => { nextCalled = true; return Task.CompletedTask; }));

        // Assert — all entries evaluated in order, next reached because all returned true
        Assert.Equal([1, 2, 3], callOrder);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenPredicateReturningFalse_WhenInvoked_ThenShortCircuits()
    {
        // Arrange
        var module = new FilterMiddleware<string>();
        module.AddPredicate((_, _) => ValueTask.FromResult(false));

        // Act
        var context = BuildContext("msg", new ServiceCollection().BuildServiceProvider());
        var nextCalled = false;
        await module.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => { nextCalled = true; return Task.CompletedTask; }));

        // Assert
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task GivenAllPredicatesReturningTrue_WhenInvoked_ThenCallsNext()
    {
        // Arrange
        var module = new FilterMiddleware<string>();
        module.AddPredicate((_, _) => ValueTask.FromResult(true));
        module.AddPredicate((_, _) => ValueTask.FromResult(true));

        // Act
        var context = BuildContext("msg", new ServiceCollection().BuildServiceProvider());
        var nextCalled = false;
        await module.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => { nextCalled = true; return Task.CompletedTask; }));

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenLaterPredicateReturnsFalse_WhenInvoked_ThenSubsequentPredicatesNotEvaluated()
    {
        // Arrange — verify short-circuit on first false
        var module = new FilterMiddleware<string>();
        var thirdCalled = false;
        module.AddPredicate((_, _) => ValueTask.FromResult(true));
        module.AddPredicate((_, _) => ValueTask.FromResult(false));
        module.AddPredicate((_, _) => { thirdCalled = true; return ValueTask.FromResult(true); });

        // Act
        var context = BuildContext("msg", new ServiceCollection().BuildServiceProvider());
        await module.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert
        Assert.False(thirdCalled);
    }

    // ── Class-based filter registration ──

    [Fact]
    public void GivenFilterTypeRegistration_WhenRegisterServicesCalled_ThenTypeRegisteredAsTransient()
    {
        // Arrange
        var module = new FilterMiddleware<string>();
        module.AddFilterType<AlwaysPassFilter>();
        var services = new ServiceCollection();

        // Act
        module.RegisterServices(services);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(AlwaysPassFilter));
    }

    [Fact]
    public async Task GivenClassBasedFilter_WhenInvoked_ThenResolvedFromServicesAndCallsNext()
    {
        // Arrange
        var module = new FilterMiddleware<string>();
        module.AddFilterType<AlwaysPassFilter>();

        var services = new ServiceCollection();
        services.AddTransient<AlwaysPassFilter>();
        var provider = services.BuildServiceProvider();
        var context = BuildContext("msg", provider);

        // Act
        var nextCalled = false;
        await module.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => { nextCalled = true; return Task.CompletedTask; }));

        // Assert
        Assert.True(nextCalled);
    }

    // ── Mixed registration ──

    [Fact]
    public async Task GivenMixedPredicateAndTypeEntries_WhenInvoked_ThenBothAreEvaluated()
    {
        // Arrange
        var predicateCalled = false;
        var module = new FilterMiddleware<string>();
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
        var nextCalled = false;
        await module.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => { nextCalled = true; return Task.CompletedTask; }));

        // Assert
        Assert.True(nextCalled);
        Assert.True(predicateCalled);
    }

    [Fact]
    public void GivenNoEntries_WhenHasEntriesChecked_ThenReturnsFalse()
    {
        // Arrange
        var module = new FilterMiddleware<string>();

        // Assert
        Assert.False(module.HasEntries);
    }

    [Fact]
    public void GivenEntryAdded_WhenHasEntriesChecked_ThenReturnsTrue()
    {
        // Arrange
        var module = new FilterMiddleware<string>();
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

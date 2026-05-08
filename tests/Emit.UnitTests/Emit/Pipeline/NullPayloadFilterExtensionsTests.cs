namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using global::Emit.Pipeline.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class NullPayloadFilterExtensionsTests
{
    [Fact]
    public void GivenSkipNullPayloads_WhenRegistered_ThenRoutesThroughBuilderFilter()
    {
        // Arrange
        var builder = new TestGroupBuilder();

        // Act
        builder.SkipNullPayloads();

        // Assert — a single async predicate filter was registered on the builder
        Assert.Equal(1, builder.RegisteredPredicateCount);
    }

    [Fact]
    public async Task GivenSkipNullPayloads_WhenMessageIsNull_ThenPredicateReturnsFalse()
    {
        // Arrange
        var builder = new TestGroupBuilder();
        builder.SkipNullPayloads();

        var services = new ServiceCollection().BuildServiceProvider();
        var context = BuildContext(null!, services);

        // Act
        var passes = await builder.LastPredicate!(context, CancellationToken.None);

        // Assert
        Assert.False(passes);
    }

    [Fact]
    public async Task GivenSkipNullPayloads_WhenMessageIsNotNull_ThenPredicateReturnsTrue()
    {
        // Arrange
        var builder = new TestGroupBuilder();
        builder.SkipNullPayloads();

        var services = new ServiceCollection().BuildServiceProvider();
        var context = BuildContext("hello", services);

        // Act
        var passes = await builder.LastPredicate!(context, CancellationToken.None);

        // Assert
        Assert.True(passes);
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

    /// <summary>
    /// Minimal <see cref="IConsumerGroupConfigurable{TMessage}"/> that records the most recent
    /// predicate registered through <c>Filter</c> so the test can invoke it directly.
    /// </summary>
    private sealed class TestGroupBuilder : IConsumerGroupConfigurable<string>
    {
        public Func<ConsumeContext<string>, CancellationToken, ValueTask<bool>>? LastPredicate { get; private set; }

        public int RegisteredPredicateCount { get; private set; }

        public IMessagePipelineBuilder InboundPipeline { get; } = new MessagePipelineBuilder();

        public IConsumerGroupConfigurable<string> OnError(Action<ErrorPolicyBuilder> configure) => this;

        public IConsumerGroupConfigurable<string> Validate<TValidator>(Action<ErrorActionBuilder> configureAction)
            where TValidator : class, IMessageValidator<string> => this;

        public IConsumerGroupConfigurable<string> Validate(
            Func<string, CancellationToken, Task<MessageValidationResult>> validator,
            Action<ErrorActionBuilder> configureAction) => this;

        public IConsumerGroupConfigurable<string> Validate(
            Func<string, MessageValidationResult> validator,
            Action<ErrorActionBuilder> configureAction) => this;

        public IInboundConfigurable<string> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
            where TMiddleware : class, IMiddleware<ConsumeContext<string>> => this;

        public IInboundConfigurable<string> Filter<TFilter>()
            where TFilter : class, IConsumerFilter<string> => this;

        public IInboundConfigurable<string> Filter(
            Func<ConsumeContext<string>, CancellationToken, ValueTask<bool>> predicate)
        {
            LastPredicate = predicate;
            RegisteredPredicateCount++;
            return this;
        }
    }
}

namespace Emit.UnitTests.FluentValidation;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.FluentValidation;
using Xunit;

public sealed class FluentValidationExtensionsTests
{
    [Fact]
    public void GivenDefaultParameters_WhenRegistering_ThenStandardValidatorTypeIsConfigured()
    {
        // Arrange
        var builder = new TestGroupBuilder();

        // Act
        builder.ValidateWithFluentValidation(a => a.Discard());

        // Assert
        Assert.Equal(typeof(FluentValidationMessageValidator<TestMessage>), builder.ConfiguredValidatorType);
    }

    [Fact]
    public void GivenAllowNullMessagesIsTrue_WhenRegistering_ThenNullTolerantValidatorTypeIsConfigured()
    {
        // Arrange
        var builder = new TestGroupBuilder();

        // Act
        builder.ValidateWithFluentValidation(a => a.Discard(), allowNullMessages: true);

        // Assert
        Assert.Equal(typeof(FluentValidationNullTolerantMessageValidator<TestMessage>), builder.ConfiguredValidatorType);
    }

    [Fact]
    public void GivenAllowNullMessagesIsFalse_WhenRegistering_ThenStandardValidatorTypeIsConfigured()
    {
        // Arrange
        var builder = new TestGroupBuilder();

        // Act
        builder.ValidateWithFluentValidation(a => a.Discard(), allowNullMessages: false);

        // Assert
        Assert.Equal(typeof(FluentValidationMessageValidator<TestMessage>), builder.ConfiguredValidatorType);
    }

    private sealed class TestMessage;

    /// <summary>
    /// Minimal <see cref="IConsumerGroupConfigurable{TMessage}"/> that captures which validator
    /// type was passed to <c>Validate&lt;TValidator&gt;</c> so tests can verify the extension's
    /// type-selection logic.
    /// </summary>
    private sealed class TestGroupBuilder : IConsumerGroupConfigurable<TestMessage>
    {
        public Type? ConfiguredValidatorType { get; private set; }

        public IMessagePipelineBuilder InboundPipeline { get; } = new global::Emit.Pipeline.MessagePipelineBuilder();

        public IConsumerGroupConfigurable<TestMessage> OnError(Action<ErrorPolicyBuilder> configure) => this;

        public IConsumerGroupConfigurable<TestMessage> Validate<TValidator>(Action<ErrorActionBuilder> configureAction)
            where TValidator : class, IMessageValidator<TestMessage>
        {
            ConfiguredValidatorType = typeof(TValidator);
            return this;
        }

        public IConsumerGroupConfigurable<TestMessage> Validate(
            Func<TestMessage, CancellationToken, Task<MessageValidationResult>> validator,
            Action<ErrorActionBuilder> configureAction) => this;

        public IConsumerGroupConfigurable<TestMessage> Validate(
            Func<TestMessage, MessageValidationResult> validator,
            Action<ErrorActionBuilder> configureAction) => this;

        public IInboundConfigurable<TestMessage> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
            where TMiddleware : class, IMiddleware<ConsumeContext<TestMessage>> => this;

        public IInboundConfigurable<TestMessage> Filter<TFilter>()
            where TFilter : class, IConsumerFilter<TestMessage> => this;

        public IInboundConfigurable<TestMessage> Filter(
            Func<ConsumeContext<TestMessage>, CancellationToken, ValueTask<bool>> predicate) => this;
    }
}

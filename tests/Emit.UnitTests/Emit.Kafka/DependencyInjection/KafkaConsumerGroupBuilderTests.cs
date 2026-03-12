namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.DependencyInjection;
using global::Emit.Kafka;
using global::Emit.Kafka.DependencyInjection;
using global::Emit.RateLimiting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaConsumerGroupBuilderTests
{
    private sealed class TestConsumerA : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestConsumerB : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestConsumerC : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestMiddleware : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next) => next.InvokeAsync(context);
    }

    private sealed class AnotherTestMiddleware : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next) => next.InvokeAsync(context);
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenDefaultValuesCorrect()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Equal(1, builder.WorkerCount);
        Assert.Equal(WorkerDistribution.ByKeyHash, builder.WorkerDistribution);
        Assert.Equal(32, builder.BufferSize);
        Assert.Equal(TimeSpan.FromSeconds(5), builder.CommitInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), builder.WorkerStopTimeout);
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenConsumerConfigPropertiesAreNull()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Null(builder.AutoOffsetReset);
        Assert.Null(builder.SessionTimeout);
        Assert.Null(builder.HeartbeatInterval);
        Assert.Null(builder.MaxPollInterval);
        Assert.Null(builder.FetchMinBytes);
        Assert.Null(builder.FetchMaxBytes);
        Assert.Null(builder.FetchWaitMax);
        Assert.Null(builder.MaxPartitionFetchBytes);
        Assert.Null(builder.GroupInstanceId);
        Assert.Null(builder.PartitionAssignmentStrategy);
        Assert.Null(builder.IsolationLevel);
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenConsumerTypesIsEmpty()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Empty(builder.ConsumerTypes);
    }

    [Fact]
    public void GivenAddConsumer_WhenCalledOnce_ThenConsumerTypeIsRegistered()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.AddConsumer<TestConsumerA>();

        // Assert
        Assert.Single(builder.ConsumerTypes);
        Assert.Equal(typeof(TestConsumerA), builder.ConsumerTypes[0]);
    }

    [Fact]
    public void GivenAddConsumer_WhenDuplicateConsumerType_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.AddConsumer<TestConsumerA>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.AddConsumer<TestConsumerA>());
    }

    [Fact]
    public void GivenAddConsumer_WhenOrderMatters_ThenPreservesRegistrationOrder()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.AddConsumer<TestConsumerA>();
        builder.AddConsumer<TestConsumerB>();
        builder.AddConsumer<TestConsumerC>();

        // Assert
        Assert.Equal(3, builder.ConsumerTypes.Count);
        Assert.Equal(typeof(TestConsumerA), builder.ConsumerTypes[0]);
        Assert.Equal(typeof(TestConsumerB), builder.ConsumerTypes[1]);
        Assert.Equal(typeof(TestConsumerC), builder.ConsumerTypes[2]);
    }

    [Fact]
    public void GivenApplyTo_WhenPropertiesSet_ThenDelegatesToConsumerConfig()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>
        {
            AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest,
            SessionTimeout = TimeSpan.FromSeconds(10)
        };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        builder.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.Equal(10000, config.SessionTimeoutMs);
    }

    [Fact]
    public void GivenApplyTo_WhenNoPropertiesSet_ThenConsumerConfigUnchanged()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        var config = new ConfluentKafka.ConsumerConfig
        {
            AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Latest,
            SessionTimeoutMs = 6000
        };

        // Act
        builder.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.AutoOffsetReset.Latest, config.AutoOffsetReset);
        Assert.Equal(6000, config.SessionTimeoutMs);
    }

    [Fact]
    public void GivenAddConsumerWithConfigure_WhenCalledOnce_ThenConsumerTypeIsRegistered()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.AddConsumer<TestConsumerA>(c => c.Use<TestMiddleware>());

        // Assert
        Assert.Single(builder.ConsumerTypes);
        Assert.Equal(typeof(TestConsumerA), builder.ConsumerTypes[0]);
    }

    [Fact]
    public void GivenAddConsumerWithConfigure_WhenMiddlewareRegistered_ThenConsumerPipelineStored()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.AddConsumer<TestConsumerA>(c => c.Use<TestMiddleware>());

        // Assert
        Assert.True(builder.ConsumerPipelines.ContainsKey(typeof(TestConsumerA)));
        Assert.Single(builder.ConsumerPipelines[typeof(TestConsumerA)].Descriptors);
    }

    [Fact]
    public void GivenAddConsumerWithConfigure_WhenNoMiddlewareRegistered_ThenConsumerPipelineNotStored()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.AddConsumer<TestConsumerA>(_ => { });

        // Assert
        Assert.Single(builder.ConsumerTypes);
        Assert.Empty(builder.ConsumerPipelines);
    }

    [Fact]
    public void GivenAddConsumerWithConfigure_WhenDuplicateConsumerType_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.AddConsumer<TestConsumerA>(c => c.Use<TestMiddleware>());

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.AddConsumer<TestConsumerA>(c => c.Use<TestMiddleware>()));
    }

    [Fact]
    public void GivenAddConsumerWithConfigure_WhenDuplicateWithNoArgOverload_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.AddConsumer<TestConsumerA>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.AddConsumer<TestConsumerA>(c => c.Use<TestMiddleware>()));
    }

    [Fact]
    public void GivenAddConsumerWithConfigure_WhenNullConfigure_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddConsumer<TestConsumerA>(null!));
    }

    [Fact]
    public void GivenAddConsumer_WhenMixedOverloads_ThenPreservesRegistrationOrderAndPipelines()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.AddConsumer<TestConsumerA>();
        builder.AddConsumer<TestConsumerB>(c => c.Use<TestMiddleware>());
        builder.AddConsumer<TestConsumerC>(c => c.Use<TestMiddleware>().Use<AnotherTestMiddleware>());

        // Assert
        Assert.Equal(3, builder.ConsumerTypes.Count);
        Assert.Equal(typeof(TestConsumerA), builder.ConsumerTypes[0]);
        Assert.Equal(typeof(TestConsumerB), builder.ConsumerTypes[1]);
        Assert.Equal(typeof(TestConsumerC), builder.ConsumerTypes[2]);

        Assert.False(builder.ConsumerPipelines.ContainsKey(typeof(TestConsumerA)));
        Assert.Single(builder.ConsumerPipelines[typeof(TestConsumerB)].Descriptors);
        Assert.Equal(2, builder.ConsumerPipelines[typeof(TestConsumerC)].Descriptors.Count);
    }

    // ── OnError tests ──

    [Fact]
    public void GivenOnError_WhenCalled_ThenStoresGroupErrorPolicyAction()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.OnError(policy => policy.Default(a => a.Discard()));

        // Assert
        Assert.NotNull(builder.GroupErrorPolicyAction);
    }

    [Fact]
    public void GivenOnError_WhenChaining_ThenReturnsSameBuilder()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        var result = builder.OnError(policy => policy.Default(a => a.Discard()));

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenOnErrorCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.OnError(policy => policy.Default(a => a.Discard()));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.OnError(policy => policy.Default(a => a.Discard())));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenOnError_WhenNullConfigure_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.OnError(null!));
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenGroupErrorPolicyActionIsNull()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Null(builder.GroupErrorPolicyAction);
    }

    // ── OnDeserializationError tests ──

    [Fact]
    public void GivenOnDeserializationError_WhenCalled_ThenStoresDeserializationErrorAction()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.OnDeserializationError(err => err.Discard());

        // Assert
        Assert.NotNull(builder.DeserializationErrorAction);
    }

    [Fact]
    public void GivenOnDeserializationError_WhenChaining_ThenReturnsSameBuilder()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        var result = builder.OnDeserializationError(err => err.Discard());

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenOnDeserializationErrorCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.OnDeserializationError(err => err.Discard());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.OnDeserializationError(err => err.Discard()));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenOnDeserializationError_WhenNullConfigure_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.OnDeserializationError(null!));
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenDeserializationErrorActionIsNull()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Null(builder.DeserializationErrorAction);
    }

    // ── RateLimit tests ──

    [Fact]
    public void GivenRateLimit_WhenCalled_ThenStoresRateLimitAction()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.RateLimit(rl => rl.TokenBucket(permitsPerSecond: 100, burstSize: 50));

        // Assert
        Assert.NotNull(builder.RateLimitAction);
    }

    [Fact]
    public void GivenRateLimit_WhenChaining_ThenReturnsSameBuilder()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        var result = builder.RateLimit(rl => rl.TokenBucket(permitsPerSecond: 100, burstSize: 50));

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenRateLimitCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.RateLimit(rl => rl.TokenBucket(permitsPerSecond: 100, burstSize: 50));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.RateLimit(rl => rl.FixedWindow(permits: 100, window: TimeSpan.FromMinutes(1))));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenRateLimit_WhenNullConfigure_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.RateLimit(null!));
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenRateLimitActionIsNull()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Null(builder.RateLimitAction);
    }

    // ── CircuitBreaker tests ──

    [Fact]
    public void GivenCircuitBreaker_WhenCalled_ThenStoresCircuitBreakerAction()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.CircuitBreaker(cb => cb
            .FailureThreshold(5)
            .SamplingWindow(TimeSpan.FromSeconds(30))
            .PauseDuration(TimeSpan.FromSeconds(10)));

        // Assert
        Assert.NotNull(builder.CircuitBreakerAction);
    }

    [Fact]
    public void GivenCircuitBreaker_WhenChaining_ThenReturnsSameBuilder()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        var result = builder.CircuitBreaker(cb => cb
            .FailureThreshold(5)
            .SamplingWindow(TimeSpan.FromSeconds(30))
            .PauseDuration(TimeSpan.FromSeconds(10)));

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenCircuitBreakerCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.CircuitBreaker(cb => cb
            .FailureThreshold(5)
            .SamplingWindow(TimeSpan.FromSeconds(30))
            .PauseDuration(TimeSpan.FromSeconds(10)));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.CircuitBreaker(cb => cb
                .FailureThreshold(3)
                .SamplingWindow(TimeSpan.FromSeconds(15))
                .PauseDuration(TimeSpan.FromSeconds(5))));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenCircuitBreaker_WhenNullConfigure_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.CircuitBreaker(null!));
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenCircuitBreakerActionIsNull()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Null(builder.CircuitBreakerAction);
    }

    // ── Group-level Validate tests ──

    [Fact]
    public void GivenValidateClassBased_WhenCalled_ThenStoresValidationModule()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.Validate<TestValidator>();

        // Assert
        Assert.NotNull(builder.Validation);
        Assert.True(builder.Validation.IsConfigured);
    }

    [Fact]
    public void GivenValidateAsyncDelegate_WhenCalled_ThenStoresValidationModule()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.Validate((_, _) => Task.FromResult(MessageValidationResult.Success));

        // Assert
        Assert.NotNull(builder.Validation);
        Assert.True(builder.Validation.IsConfigured);
    }

    [Fact]
    public void GivenValidateSyncDelegate_WhenCalled_ThenStoresValidationModule()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.Validate(_ => MessageValidationResult.Success);

        // Assert
        Assert.NotNull(builder.Validation);
        Assert.True(builder.Validation.IsConfigured);
    }

    [Fact]
    public void GivenValidate_WhenChaining_ThenReturnsSameBuilder()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        var result = builder.Validate<TestValidator>();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenValidateCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();
        builder.Validate<TestValidator>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.Validate<TestValidator>());
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenValidateAsyncDelegate_WhenNullValidator_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => builder.Validate((Func<string, CancellationToken, Task<MessageValidationResult>>)null!));
    }

    [Fact]
    public void GivenValidateSyncDelegate_WhenNullValidator_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => builder.Validate((Func<string, MessageValidationResult>)null!));
    }

    [Fact]
    public void GivenNewBuilder_WhenDefaultState_ThenValidationIsNull()
    {
        // Arrange & Act
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Assert
        Assert.Null(builder.Validation);
    }

    private sealed class TestValidator : IMessageValidator<string>
    {
        public Task<MessageValidationResult> ValidateAsync(string message, CancellationToken cancellationToken)
            => Task.FromResult(MessageValidationResult.Success);
    }
}

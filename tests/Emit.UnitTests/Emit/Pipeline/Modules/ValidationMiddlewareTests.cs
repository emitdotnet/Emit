namespace Emit.UnitTests.Pipeline.Modules;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Metrics;
using global::Emit.Pipeline.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class ValidationMiddlewareTests
{
    // ── Helpers ──

    private static IServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new EmitMetrics(null, new EmitMetricsEnrichment()));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return services;
    }

    private static ConsumeContext<string> CreateContext(IServiceProvider services)
    {
        return new ConsumeContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            Message = "test-message",
            TransportContext = TestTransportContext.Create(services),
        };
    }

    private static TestPipeline<ConsumeContext<string>> RecordingNext(out Action assertCalled)
    {
        var called = false;
        assertCalled = () => Assert.True(called, "Expected next pipeline to be invoked, but it was not.");
        return new TestPipeline<ConsumeContext<string>>(_ => { called = true; return Task.CompletedTask; });
    }

    private static TestPipeline<ConsumeContext<string>> RecordingNextNotCalled(out Action assertNotCalled)
    {
        var called = false;
        assertNotCalled = () => Assert.False(called, "Expected next pipeline NOT to be invoked, but it was.");
        return new TestPipeline<ConsumeContext<string>>(_ => { called = true; return Task.CompletedTask; });
    }

    // ── Tests ──

    [Fact]
    public async Task GivenValidMessage_WhenInvoked_ThenCallsNextMiddleware()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.Discard());
        var services = BaseServices().BuildServiceProvider();
        var next = RecordingNext(out var assertCalled);

        // Act
        await module.InvokeAsync(CreateContext(services), next);

        // Assert
        assertCalled();
    }

    [Fact]
    public async Task GivenInvalidMessage_WhenInvoked_ThenDoesNotCallNext()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Fail("invalid")), a => a.Discard());
        var services = BaseServices().BuildServiceProvider();
        var next = RecordingNextNotCalled(out var assertNotCalled);

        // Act
        await module.InvokeAsync(CreateContext(services), next);

        // Assert
        assertNotCalled();
    }

    [Fact]
    public async Task GivenInvalidMessageAndDeadLetterAction_WhenInvoked_ThenDeadLettersInline()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Fail("field is required")), a => a.DeadLetter());
        var sink = new RecordingDeadLetterSink();
        var services = BaseServices();
        services.AddSingleton<IDeadLetterSink>(sink);
        var sp = services.BuildServiceProvider();

        // Act
        await module.InvokeAsync(CreateContext(sp), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert
        Assert.Single(sink.ProducedMessages);
        var headers = sink.ProducedMessages[0].Headers;
        Assert.Contains(headers, h => h.Key == DeadLetterHeaders.ExceptionType
            && h.Value.Contains(nameof(MessageValidationException), StringComparison.Ordinal));
        Assert.Contains(headers, h => h.Key == DeadLetterHeaders.ExceptionMessage
            && h.Value.Contains("field is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenInvalidMessageAndDiscardAction_WhenInvoked_ThenDoesNotDeadLetter()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Fail("invalid")), a => a.Discard());
        var sink = new RecordingDeadLetterSink();
        var services = BaseServices();
        services.AddSingleton<IDeadLetterSink>(sink);
        var sp = services.BuildServiceProvider();

        // Act
        await module.InvokeAsync(CreateContext(sp), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert
        Assert.Empty(sink.ProducedMessages);
    }

    [Fact]
    public async Task GivenInvalidMessageAndDeadLetterActionWithoutSink_WhenInvoked_ThenSilentlySkips()
    {
        // Arrange — no sink registered
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Fail("invalid")), a => a.DeadLetter());
        var services = BaseServices().BuildServiceProvider();
        var next = RecordingNextNotCalled(out var assertNotCalled);

        // Act
        await module.InvokeAsync(CreateContext(services), next);

        // Assert — short-circuits cleanly even without a sink
        assertNotCalled();
    }

    [Fact]
    public async Task GivenValidatorThrowsException_WhenInvoked_ThenExceptionPropagates()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) =>
            throw new TimeoutException("database unavailable"), a => a.Discard());
        var services = BaseServices().BuildServiceProvider();

        // Act & Assert — transient validator errors propagate so the outer error policy can retry.
        await Assert.ThrowsAsync<TimeoutException>(
            () => module.InvokeAsync(CreateContext(services), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)));
    }

    [Fact]
    public async Task GivenMultipleValidationErrors_WhenInvokedWithDeadLetter_ThenAllErrorsInDlqHeader()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(
            MessageValidationResult.Fail(["name is required", "age must be positive", "email is invalid"])), a => a.DeadLetter());
        var sink = new RecordingDeadLetterSink();
        var services = BaseServices();
        services.AddSingleton<IDeadLetterSink>(sink);
        var sp = services.BuildServiceProvider();

        // Act
        await module.InvokeAsync(CreateContext(sp), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert
        Assert.Single(sink.ProducedMessages);
        var exceptionMessage = sink.ProducedMessages[0].Headers
            .First(h => h.Key == DeadLetterHeaders.ExceptionMessage).Value;
        Assert.Contains("name is required", exceptionMessage, StringComparison.Ordinal);
        Assert.Contains("age must be positive", exceptionMessage, StringComparison.Ordinal);
        Assert.Contains("email is invalid", exceptionMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GivenInlineDelegateValidator_WhenInvoked_ThenDelegateExecutes()
    {
        // Arrange
        var delegateCalled = false;
        var module = new ValidationMiddleware<string>();
        module.Configure((msg, _) =>
        {
            delegateCalled = true;
            Assert.Equal("test-message", msg);
            return Task.FromResult(MessageValidationResult.Success);
        }, a => a.Discard());
        var services = BaseServices().BuildServiceProvider();

        // Act
        await module.InvokeAsync(CreateContext(services), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert
        Assert.True(delegateCalled);
    }

    [Fact]
    public async Task GivenClassBasedValidator_WhenInvoked_ThenValidatorResolvedFromDI()
    {
        // Arrange
        var services = BaseServices();
        services.AddScoped<StubValidator>();
        var sp = services.BuildServiceProvider();

        var module = new ValidationMiddleware<string>();
        module.Configure<StubValidator>(a => a.Discard());

        // Act
        await module.InvokeAsync(CreateContext(sp), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert — validator was resolved and invoked (StubValidator always returns Success)
        var validator = sp.GetRequiredService<StubValidator>();
        Assert.True(validator.WasCalled);
    }

    [Fact]
    public void GivenConfigureWithDiscard_WhenConfigured_ThenValidationErrorActionIsDiscard()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();

        // Act
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.Discard());

        // Assert
        Assert.NotNull(module.ValidationErrorAction);
        Assert.IsType<ErrorAction.DiscardAction>(module.ValidationErrorAction);
    }

    [Fact]
    public void GivenConfigureWithDeadLetter_WhenConfigured_ThenValidationErrorActionIsDeadLetter()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();

        // Act
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.DeadLetter());

        // Assert
        Assert.NotNull(module.ValidationErrorAction);
        Assert.IsType<ErrorAction.DeadLetterAction>(module.ValidationErrorAction);
    }

    [Fact]
    public void GivenClassBasedValidator_WhenRegisterServices_ThenValidatorTypeRegistered()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure<StubValidator>(a => a.Discard());
        var services = new ServiceCollection();

        // Act
        module.RegisterServices(services);
        var sp = services.BuildServiceProvider();

        // Assert
        var validator = sp.GetService<StubValidator>();
        Assert.NotNull(validator);
    }

    [Fact]
    public void GivenDelegateValidator_WhenRegisterServices_ThenNoServiceRegistered()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.Discard());
        var services = new ServiceCollection();
        var countBefore = services.Count;

        // Act
        module.RegisterServices(services);

        // Assert
        Assert.Equal(countBefore, services.Count);
    }

    [Fact]
    public void GivenAlreadyConfigured_WhenConfigureAgain_ThenThrows()
    {
        // Arrange
        var module = new ValidationMiddleware<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.Discard());

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.Discard()));
    }

    // ── Test infrastructure ──

    internal sealed class StubValidator : IMessageValidator<string>
    {
        public bool WasCalled { get; private set; }

        public Task<MessageValidationResult> ValidateAsync(string message, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(MessageValidationResult.Success);
        }
    }

    private sealed class RecordingDeadLetterSink : IDeadLetterSink
    {
        public List<RecordedMessage> ProducedMessages { get; } = [];

        public Uri DestinationAddress { get; } = new("emit://dlq/test");

        public Task ProduceAsync(
            byte[]? rawKey,
            byte[]? rawValue,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            CancellationToken cancellationToken)
        {
            ProducedMessages.Add(new RecordedMessage(rawKey, rawValue, headers.ToList()));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedMessage(
        byte[]? Key,
        byte[]? Value,
        IReadOnlyList<KeyValuePair<string, string>> Headers);
}

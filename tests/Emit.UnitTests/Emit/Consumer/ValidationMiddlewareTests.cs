namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Pipeline.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class ValidationMiddlewareTests
{
    private readonly ILogger<ValidationMiddleware<string>> logger =
        NullLogger<ValidationMiddleware<string>>.Instance;

    // ── Helpers ──

    private static ConsumeContext<string> CreateContext(IServiceProvider? services = null)
    {
        var svc = services ?? new ServiceCollection().BuildServiceProvider();
        return new ConsumeContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = svc,
            Message = "test-message",
            TransportContext = TestTransportContext.Create(svc),
        };
    }

    private ValidationMiddleware<string> CreateMiddleware(ValidationModule<string> validation)
    {
        return new ValidationMiddleware<string>(
            validation,
            new EmitMetrics(null, new EmitMetricsEnrichment()),
            logger);
    }

    // ── Tests ──

    [Fact]
    public async Task GivenValidMessage_WhenInvoked_ThenCallsNextMiddleware()
    {
        // Arrange
        var module = new ValidationModule<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.Discard());
        var middleware = CreateMiddleware(module);
        var nextCalled = false;

        // Act
        await middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => { nextCalled = true; return Task.CompletedTask; }));

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenInvalidMessage_WhenInvoked_ThenThrowsMessageValidationException()
    {
        // Arrange
        var module = new ValidationModule<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Fail("field is required")), a => a.Discard());
        var middleware = CreateMiddleware(module);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<MessageValidationException>(
            () => middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)));
        Assert.Contains("field is required", ex.Errors);
    }

    [Fact]
    public async Task GivenInvalidMessage_WhenInvoked_ThenDoesNotCallNext()
    {
        // Arrange
        var module = new ValidationModule<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Fail("invalid")), a => a.Discard());
        var middleware = CreateMiddleware(module);
        var nextCalled = false;

        // Act — catch expected exception
        try
        {
            await middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => { nextCalled = true; return Task.CompletedTask; }));
        }
        catch (MessageValidationException)
        {
            // expected
        }

        // Assert
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task GivenValidatorThrowsException_WhenInvoked_ThenExceptionPropagates()
    {
        // Arrange
        var module = new ValidationModule<string>();
        module.Configure((_, _) =>
            throw new TimeoutException("database unavailable"), a => a.Discard());
        var middleware = CreateMiddleware(module);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(
            () => middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)));
    }

    [Fact]
    public async Task GivenMultipleValidationErrors_WhenInvoked_ThenExceptionContainsAllErrors()
    {
        // Arrange
        var module = new ValidationModule<string>();
        module.Configure((_, _) => Task.FromResult(
            MessageValidationResult.Fail(["name is required", "age must be positive", "email is invalid"])), a => a.Discard());
        var middleware = CreateMiddleware(module);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<MessageValidationException>(
            () => middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)));
        Assert.Equal(3, ex.Errors.Count);
        Assert.Contains("name is required", ex.Errors);
        Assert.Contains("age must be positive", ex.Errors);
        Assert.Contains("email is invalid", ex.Errors);
    }

    [Fact]
    public async Task GivenInlineDelegateValidator_WhenInvoked_ThenDelegateExecutes()
    {
        // Arrange
        var delegateCalled = false;
        var module = new ValidationModule<string>();
        module.Configure((msg, _) =>
        {
            delegateCalled = true;
            Assert.Equal("test-message", msg);
            return Task.FromResult(MessageValidationResult.Success);
        }, a => a.Discard());
        var middleware = CreateMiddleware(module);

        // Act
        await middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert
        Assert.True(delegateCalled);
    }

    [Fact]
    public async Task GivenClassBasedValidator_WhenInvoked_ThenValidatorResolvedFromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<StubValidator>();
        var sp = services.BuildServiceProvider();

        var module = new ValidationModule<string>();
        module.Configure<StubValidator>(a => a.Discard());
        var middleware = CreateMiddleware(module);
        var context = CreateContext(sp);

        // Act
        await middleware.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask));

        // Assert — validator was resolved and invoked (StubValidator always returns Success)
        var validator = sp.GetRequiredService<StubValidator>();
        Assert.True(validator.WasCalled);
    }

    [Fact]
    public void GivenConfigureWithDiscard_WhenConfigured_ThenValidationErrorActionIsDiscard()
    {
        // Arrange
        var module = new ValidationModule<string>();

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
        var module = new ValidationModule<string>();

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
        var module = new ValidationModule<string>();
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
        var module = new ValidationModule<string>();
        module.Configure((_, _) => Task.FromResult(MessageValidationResult.Success), a => a.Discard());
        var services = new ServiceCollection();
        var countBefore = services.Count;

        // Act
        module.RegisterServices(services);

        // Assert
        Assert.Equal(countBefore, services.Count);
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
}

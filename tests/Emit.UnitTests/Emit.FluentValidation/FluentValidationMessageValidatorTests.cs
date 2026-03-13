namespace Emit.UnitTests.FluentValidation;

using Emit.Abstractions;
using Emit.FluentValidation;
using global::FluentValidation;
using global::FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class FluentValidationMessageValidatorTests
{
    private sealed record TestOrder(string Name, int Amount);

    private sealed class TestOrderValidator : AbstractValidator<TestOrder>
    {
        public TestOrderValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Amount).GreaterThan(0);
        }
    }

    private sealed class AlwaysPassValidator : AbstractValidator<TestOrder>
    {
        // No rules — always valid
    }

    private sealed class NameOnlyValidator : AbstractValidator<TestOrder>
    {
        public NameOnlyValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        }
    }

    private sealed class CapturingValidator : IValidator<TestOrder>
    {
        public CancellationToken CapturedToken { get; private set; }

        public Task<ValidationResult> ValidateAsync(
            IValidationContext context, CancellationToken cancellation = default)
        {
            CapturedToken = cancellation;
            return Task.FromResult(new ValidationResult());
        }

        public ValidationResult Validate(IValidationContext context) => new();
        public ValidationResult Validate(TestOrder instance) => new();
        public Task<ValidationResult> ValidateAsync(
            TestOrder instance, CancellationToken cancellation = default)
        {
            CapturedToken = cancellation;
            return Task.FromResult(new ValidationResult());
        }

        public IValidatorDescriptor CreateDescriptor() =>
            new ValidatorDescriptor<TestOrder>([]);

        public bool CanValidateInstancesOfType(Type type) => type == typeof(TestOrder);
    }

    private sealed class ThrowingValidator : IValidator<TestOrder>
    {
        public Task<ValidationResult> ValidateAsync(
            IValidationContext context, CancellationToken cancellation = default) =>
            throw new TimeoutException("validator timed out");

        public ValidationResult Validate(IValidationContext context) =>
            throw new TimeoutException("validator timed out");

        public ValidationResult Validate(TestOrder instance) =>
            throw new TimeoutException("validator timed out");

        public Task<ValidationResult> ValidateAsync(
            TestOrder instance, CancellationToken cancellation = default) =>
            throw new TimeoutException("validator timed out");

        public IValidatorDescriptor CreateDescriptor() =>
            new ValidatorDescriptor<TestOrder>([]);

        public bool CanValidateInstancesOfType(Type type) => type == typeof(TestOrder);
    }

    private static FluentValidationMessageValidator<TestOrder> BuildValidator(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return new FluentValidationMessageValidator<TestOrder>(provider);
    }

    [Fact]
    public async Task GivenValidMessage_WhenValidated_ThenReturnsSuccess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IValidator<TestOrder>, AlwaysPassValidator>();
        var validator = BuildValidator(services);
        var message = new TestOrder("Widget", 5);

        // Act
        var result = await validator.ValidateAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Same(MessageValidationResult.Success, result);
    }

    [Fact]
    public async Task GivenInvalidMessage_WhenValidated_ThenReturnsFailWithErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IValidator<TestOrder>, NameOnlyValidator>();
        var validator = BuildValidator(services);
        var message = new TestOrder("", 10);

        // Act
        var result = await validator.ValidateAsync(message, CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Name is required", result.Errors);
    }

    [Fact]
    public async Task GivenMultipleRuleFailures_WhenValidated_ThenAllErrorsCollected()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IValidator<TestOrder>, TestOrderValidator>();
        var validator = BuildValidator(services);
        var message = new TestOrder("", -1);

        // Act
        var result = await validator.ValidateAsync(message, CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("Name") || e.Contains("'Name'"));
        Assert.Contains(result.Errors, e => e.Contains("Amount") || e.Contains("'Amount'"));
    }

    [Fact]
    public async Task GivenCancellationToken_WhenValidated_ThenTokenForwardedToFluentValidation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var capturingValidator = new CapturingValidator();

        var services = new ServiceCollection();
        services.AddScoped<IValidator<TestOrder>>(_ => capturingValidator);
        var validator = BuildValidator(services);
        var message = new TestOrder("Widget", 5);

        // Act
        await validator.ValidateAsync(message, token);

        // Assert
        Assert.Equal(token, capturingValidator.CapturedToken);
    }

    [Fact]
    public async Task GivenNoValidatorRegistered_WhenValidated_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var validator = BuildValidator(services);
        var message = new TestOrder("Widget", 5);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(message, CancellationToken.None));

        Assert.Contains(nameof(TestOrder), ex.Message);
        Assert.Contains("FluentValidation", ex.Message);
    }

    [Fact]
    public async Task GivenValidatorThrows_WhenValidated_ThenExceptionPropagates()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IValidator<TestOrder>, ThrowingValidator>();
        var validator = BuildValidator(services);
        var message = new TestOrder("Widget", 5);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => validator.ValidateAsync(message, CancellationToken.None));

        Assert.Equal("validator timed out", ex.Message);
    }
}

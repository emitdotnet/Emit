namespace Emit.Resilience;

using FluentValidation;

/// <summary>
/// Validates <see cref="ResiliencePolicy"/> configuration.
/// </summary>
/// <remarks>
/// Validation is performed at registration time to ensure invalid configurations
/// fail fast with clear error messages rather than causing runtime issues.
/// </remarks>
internal sealed class ResiliencePolicyValidator : AbstractValidator<ResiliencePolicy>
{
    /// <summary>
    /// Minimum allowed value for <see cref="ResiliencePolicy.BackoffBaseDelay"/>.
    /// </summary>
    public static readonly TimeSpan MinBackoffBaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Minimum allowed value for <see cref="ResiliencePolicy.CircuitBreakerCooldown"/>.
    /// </summary>
    public static readonly TimeSpan MinCircuitBreakerCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="ResiliencePolicyValidator"/> class.
    /// </summary>
    public ResiliencePolicyValidator()
    {
        RuleFor(x => x.MaxRetryCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Max retry count must be non-negative. Use 0 to disable retries.");

        RuleFor(x => x.BackoffBaseDelay)
            .GreaterThanOrEqualTo(MinBackoffBaseDelay)
            .WithMessage($"Backoff base delay must be at least {MinBackoffBaseDelay.TotalSeconds} second(s).");

        RuleFor(x => x.MaxBackoffDelay)
            .GreaterThan(x => x.BackoffBaseDelay)
            .WithMessage("Max backoff delay must be greater than base delay.");

        RuleFor(x => x.CircuitBreakerFailureThreshold)
            .GreaterThan(0)
            .WithMessage("Circuit breaker failure threshold must be positive.");

        RuleFor(x => x.CircuitBreakerCooldown)
            .GreaterThanOrEqualTo(MinCircuitBreakerCooldown)
            .WithMessage($"Circuit breaker cooldown must be at least {MinCircuitBreakerCooldown.TotalSeconds} seconds.");
    }
}

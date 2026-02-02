namespace Emit.DependencyInjection;

using Emit.Configuration;
using Emit.Resilience;
using Emit.Worker;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Extension methods for adding Emit services to the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Emit outbox services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration action for the Emit builder.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no persistence provider is registered, if both MongoDB and PostgreSQL
    /// are registered, or if no outbox provider is registered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method configures the Emit transactional outbox system. You must:
    /// <list type="bullet">
    /// <item><description>Register exactly one persistence provider (MongoDB or PostgreSQL)</description></item>
    /// <item><description>Register at least one outbox provider (e.g., Kafka)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// services.AddEmit(builder =&gt;
    /// {
    ///     builder.UseMongoDb((sp, options) =&gt;
    ///     {
    ///         options.ConnectionString = "mongodb://localhost:27017";
    ///         options.DatabaseName = "myapp";
    ///     });
    ///
    ///     builder.AddKafka((sp, kafka) =&gt;
    ///     {
    ///         kafka.AddProducer&lt;string, OrderCreated&gt;(producer =&gt;
    ///         {
    ///             producer.Topic = "orders";
    ///         });
    ///     });
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEmit(
        this IServiceCollection services,
        Action<EmitBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EmitBuilder(services);
        configure(builder);
        builder.Validate();

        // Register configuration options with validation
        services.AddOptions<EmitOptions>()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmitOptions>, FluentValidateOptions<EmitOptions, EmitOptionsValidator>>();

        services.AddOptions<WorkerOptions>()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkerOptions>, FluentValidateOptions<WorkerOptions, WorkerOptionsValidator>>();

        services.AddOptions<CleanupOptions>()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CleanupOptions>, FluentValidateOptions<CleanupOptions, CleanupOptionsValidator>>();

        // Register resilience policy (global or default)
        var globalPolicy = builder.BuildGlobalResiliencePolicy() ?? ResiliencePolicy.Default;
        services.AddSingleton(globalPolicy);

        // Register background workers
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService<CompletedEntriesCleanupWorker>();

        return services;
    }
}

/// <summary>
/// Adapts FluentValidation validators to <see cref="IValidateOptions{TOptions}"/>.
/// </summary>
/// <typeparam name="TOptions">The options type to validate.</typeparam>
/// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
internal sealed class FluentValidateOptions<TOptions, TValidator> : IValidateOptions<TOptions>
    where TOptions : class
    where TValidator : IValidator<TOptions>, new()
{
    private readonly TValidator validator = new();

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var result = validator.Validate(options);
        if (result.IsValid)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = result.Errors.Select(e => e.ErrorMessage);
        return ValidateOptionsResult.Fail(errors);
    }
}

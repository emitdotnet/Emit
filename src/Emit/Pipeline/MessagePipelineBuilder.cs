namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// The middleware registered on one level, composed with the other levels of a pipeline by
/// <see cref="Build{TPipeline,TMessage}"/>.
/// </summary>
internal abstract class MessagePipelineBuilder
{
    /// <summary>
    /// Whether nothing is registered on this level.
    /// </summary>
    internal bool IsEmpty => registrations.Count == 0;

    private protected readonly List<Registration> registrations = [];

    /// <summary>
    /// Registers the middleware types of this level in the service collection with their lifetime. Generic type
    /// definitions are registered as such, and the container closes them.
    /// </summary>
    internal void RegisterServices(IServiceCollection services)
    {
        foreach (var registration in registrations)
        {
            if (registration.MiddlewareType is null)
            {
                continue;
            }

            if (registration.Lifetime == MiddlewareLifetime.Singleton)
            {
                services.TryAddSingleton(registration.MiddlewareType);
            }
            else
            {
                services.TryAddScoped(registration.MiddlewareType);
            }
        }
    }

    /// <summary>
    /// Checks that every middleware of this level runs on pipelines of <typeparamref name="TPipeline"/>, for a level
    /// composed into pipelines of a context other than its own, such as a consumer group's level into the pipeline
    /// of its batch consumer.
    /// </summary>
    /// <exception cref="InvalidOperationException">A middleware of this level cannot run on these pipelines.</exception>
    internal void EnsureRunsOn<TPipeline>() where TPipeline : MessageContext
    {
        foreach (var registration in registrations)
        {
            if (registration.MiddlewareType is not { IsGenericTypeDefinition: false } type
                || !typeof(IMiddleware<TPipeline>).IsAssignableFrom(type))
            {
                var middleware = registration.MiddlewareType is null
                    ? "A middleware factory"
                    : $"'{DisplayName(registration.MiddlewareType)}'";
                throw new InvalidOperationException(
                    $"{middleware} cannot run on pipelines of {DisplayName(typeof(TPipeline))}. Register with " +
                    $"Use<TMiddleware>() a middleware of {DisplayName(typeof(TPipeline))} or of a context it derives from.");
            }
        }
    }

    /// <summary>
    /// Composes the middleware of <paramref name="levels"/>, outermost first, around <paramref name="terminal"/>.
    /// </summary>
    /// <typeparam name="TPipeline">The context of the pipeline.</typeparam>
    /// <typeparam name="TMessage">The message type generic type definitions are closed with.</typeparam>
    internal static IMiddlewarePipeline<TPipeline> Build<TPipeline, TMessage>(
        IServiceProvider services,
        IMiddlewarePipeline<TPipeline> terminal,
        params MessagePipelineBuilder?[] levels)
        where TPipeline : MessageContext
    {
        var next = terminal;

        for (var i = levels.Length - 1; i >= 0; i--)
        {
            if (levels[i] is not { } level)
            {
                continue;
            }

            for (var j = level.registrations.Count - 1; j >= 0; j--)
            {
                next = Compose<TPipeline, TMessage>(services, level.registrations[j], next);
            }
        }

        return next;
    }

    /// <summary>
    /// The name of <paramref name="type"/> as written in C#, such as <c>ConsumeContext&lt;Order&gt;</c>.
    /// </summary>
    private protected static string DisplayName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        // A type generic only through the type it is nested in has no arity marker in its name
        var marker = type.Name.IndexOf('`');
        var name = marker < 0 ? type.Name : type.Name[..marker];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(DisplayName))}>";
    }

    private static IMiddlewarePipeline<TPipeline> Compose<TPipeline, TMessage>(
        IServiceProvider services,
        Registration registration,
        IMiddlewarePipeline<TPipeline> next)
        where TPipeline : MessageContext
    {
        // Every level accepts only middleware of its context or of a context it derives from, and is composed into
        // pipelines whose context derives from its own, so the middleware converts to the pipeline's context by
        // contravariance
        Func<IServiceProvider, IMiddleware<TPipeline>> resolve;
        if (registration.MiddlewareType is { } type)
        {
            var closedType = type.IsGenericTypeDefinition ? type.MakeGenericType(typeof(TMessage)) : type;
            resolve = sp => (IMiddleware<TPipeline>)sp.GetRequiredService(closedType);
        }
        else
        {
            resolve = sp => (IMiddleware<TPipeline>)registration.Factory!(sp);
        }

        return registration.Lifetime == MiddlewareLifetime.Singleton
            ? new MiddlewarePipeline<TPipeline>(resolve(services), next)
            : new ScopedMiddlewarePipeline<TPipeline>(resolve, next);
    }

    /// <summary>
    /// A middleware registered on a level: a type resolved from the service provider, or a factory.
    /// </summary>
    private protected sealed record Registration(
        Type? MiddlewareType,
        Func<IServiceProvider, object>? Factory,
        MiddlewareLifetime Lifetime);

    private sealed class ScopedMiddlewarePipeline<TContext>(
        Func<IServiceProvider, IMiddleware<TContext>> resolve,
        IMiddlewarePipeline<TContext> next) : IMiddlewarePipeline<TContext>
        where TContext : MessageContext
    {
        public Task InvokeAsync(TContext context) =>
            resolve(context.Services).InvokeAsync(context, () => next.InvokeAsync(context));
    }
}

/// <summary>
/// A level of middleware for pipelines of <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The context of the pipelines this level applies to.</typeparam>
/// <param name="typedContextDefinition">
/// The generic definition of the typed context of these pipelines, such as <c>typeof(ConsumeContext&lt;&gt;)</c>,
/// that a generic middleware type definition may be written for; <see langword="null"/> when
/// <typeparamref name="TContext"/> is already the context of a single message type.
/// </param>
internal sealed class MessagePipelineBuilder<TContext>(Type? typedContextDefinition = null)
    : MessagePipelineBuilder, IMessagePipelineBuilder<TContext>
    where TContext : MessageContext
{
    /// <inheritdoc />
    public IMessagePipelineBuilder<TContext> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<TContext>
    {
        registrations.Add(new(typeof(TMiddleware), Factory: null, lifetime));
        return this;
    }

    /// <inheritdoc />
    public IMessagePipelineBuilder<TContext> Use(
        Func<IServiceProvider, IMiddleware<TContext>> factory,
        MiddlewareLifetime lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        registrations.Add(new(MiddlewareType: null, factory, lifetime));
        return this;
    }

    /// <inheritdoc />
    public IMessagePipelineBuilder<TContext> Use(Type middlewareType, MiddlewareLifetime lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(middlewareType);

        if (!middlewareType.IsGenericTypeDefinition || middlewareType.GetGenericArguments().Length != 1)
        {
            throw new ArgumentException(
                $"'{DisplayName(middlewareType)}' is not a generic type definition with one type parameter. " +
                $"Register other middleware with {nameof(Use)}<TMiddleware>().",
                nameof(middlewareType));
        }

        // Closing the definition and the typed context with the same message type must give a middleware of the
        // typed context: one of its interfaces accepts the typed context, or this level's own context
        var messageType = middlewareType.GetGenericArguments()[0];
        var typedContext = typedContextDefinition?.MakeGenericType(messageType);
        var runsOnThisLevel = middlewareType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMiddleware<>))
            .Select(i => i.GetGenericArguments()[0])
            .Any(accepted => accepted.IsAssignableFrom(typeof(TContext))
                || (typedContext is not null && accepted.IsAssignableFrom(typedContext)));

        if (!runsOnThisLevel)
        {
            var expected = DisplayName(typedContext ?? typeof(TContext));
            throw new ArgumentException(
                $"'{DisplayName(middlewareType)}' cannot run on pipelines of {DisplayName(typeof(TContext))}: it " +
                $"must implement IMiddleware<{expected}> or IMiddleware of a context {expected} derives from.",
                nameof(middlewareType));
        }

        registrations.Add(new(middlewareType, Factory: null, lifetime));
        return this;
    }
}

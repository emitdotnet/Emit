namespace Emit.Mediator.DependencyInjection;

using Emit.Abstractions.Pipeline;
using Emit.Mediator.Observability;
using Emit.Pipeline;

/// <summary>
/// Configures handler registrations for the mediator.
/// </summary>
public sealed class MediatorBuilder : IInboundConfigurable
{
    private readonly Dictionary<Type, HandlerRegistration> registrations = [];
    private readonly List<Type> observerTypes = [];

    /// <summary>
    /// Gets the mediator-level inbound middleware pipeline builder. Middleware registered here
    /// wraps all mediator request handlers.
    /// </summary>
    public IMessagePipelineBuilder InboundPipeline { get; } = new MessagePipelineBuilder();

    /// <summary>
    /// Gets the handler registrations, keyed by request type.
    /// </summary>
    internal IReadOnlyDictionary<Type, HandlerRegistration> Registrations => registrations;

    /// <summary>
    /// Gets the registered observer types.
    /// </summary>
    internal IReadOnlyList<Type> ObserverTypes => observerTypes;

    /// <summary>
    /// Creates a new mediator builder.
    /// </summary>
    internal MediatorBuilder()
    {
    }

    /// <summary>
    /// Registers a handler type. The library discovers request and response types
    /// by reflecting <see cref="IRequestHandler{TRequest}"/> and
    /// <see cref="IRequestHandler{TRequest, TResponse}"/> implementations.
    /// </summary>
    /// <typeparam name="THandler">The handler type implementing one or more handler interfaces.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// A handler is already registered for one of its request types.
    /// </exception>
    public void AddHandler<THandler>() where THandler : class, IRequestHandler
    {
        RegisterHandler<THandler>(handlerPipeline: null);
    }

    /// <summary>
    /// Registers a handler type with per-handler middleware configuration.
    /// Middleware registered here wraps only this handler, forming the innermost pipeline layer
    /// (global → mediator → per-handler → terminal). The <typeparamref name="TRequest"/> type
    /// parameter constrains the middleware builder so that only middleware compatible with the
    /// handler's request type can be registered.
    /// </summary>
    /// <typeparam name="THandler">The handler type implementing one or more handler interfaces.</typeparam>
    /// <typeparam name="TRequest">The request type handled by this handler.</typeparam>
    /// <param name="configure">Configuration action for per-handler middleware.</param>
    /// <exception cref="InvalidOperationException">
    /// A handler is already registered for <typeparamref name="TRequest"/>.
    /// </exception>
    public void AddHandler<THandler, TRequest>(Action<MediatorHandlerBuilder<TRequest>> configure)
        where THandler : class, IHandlesRequest<TRequest>
    {
        ArgumentNullException.ThrowIfNull(configure);

        var handlerType = typeof(THandler);
        var requestType = typeof(TRequest);
        var responseType = FindResponseType(handlerType, requestType);

        var handlerBuilder = new MediatorHandlerBuilder<TRequest>();
        configure(handlerBuilder);
        AddRegistration(handlerType, requestType, responseType, handlerBuilder.Pipeline);
    }

    /// <summary>
    /// Registers a mediator observer that is notified before, after, and on failure
    /// of every mediator request handling operation.
    /// </summary>
    /// <typeparam name="T">The observer type.</typeparam>
    /// <returns>This builder for method chaining.</returns>
    public MediatorBuilder AddObserver<T>() where T : class, IMediatorObserver
    {
        observerTypes.Add(typeof(T));
        return this;
    }

    private static Type? FindResponseType(Type handlerType, Type requestType)
    {
        // Check IRequestHandler<TRequest, TResponse> (response handlers)
        foreach (var iface in handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
        {
            var args = iface.GetGenericArguments();
            if (args[0] == requestType)
                return args[1];
        }

        // Check IRequestHandler<TRequest> (void handlers)
        foreach (var iface in handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
        {
            if (iface.GetGenericArguments()[0] == requestType)
                return null;
        }

        throw new InvalidOperationException(
            $"Handler type '{handlerType.Name}' does not implement " +
            $"{nameof(IRequestHandler)}<{requestType.Name}> or " +
            $"{nameof(IRequestHandler)}<{requestType.Name}, TResponse>.");
    }

    private void RegisterHandler<THandler>(IMessagePipelineBuilder? handlerPipeline) where THandler : class, IRequestHandler
    {
        var handlerType = typeof(THandler);

        // Scan for IRequestHandler<TRequest, TResponse> (response handlers)
        foreach (var iface in handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
        {
            var genericArgs = iface.GetGenericArguments();
            AddRegistration(handlerType, genericArgs[0], genericArgs[1], handlerPipeline);
        }

        // Scan for IRequestHandler<TRequest> (void handlers)
        foreach (var iface in handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
        {
            var requestType = iface.GetGenericArguments()[0];
            AddRegistration(handlerType, requestType, responseType: null, handlerPipeline);
        }
    }

    private void AddRegistration(Type handlerType, Type requestType, Type? responseType, IMessagePipelineBuilder? handlerPipeline)
    {
        if (!registrations.TryAdd(requestType, new HandlerRegistration(handlerType, requestType, responseType, handlerPipeline)))
        {
            throw new InvalidOperationException(
                $"A handler is already registered for request type '{requestType.Name}'. " +
                $"Only one handler per request type is allowed.");
        }
    }

    /// <summary>
    /// Captures the handler type, its request/response type pair, and optional per-handler pipeline.
    /// A null <see cref="ResponseType"/> indicates a void handler.
    /// </summary>
    internal sealed record HandlerRegistration(Type HandlerType, Type RequestType, Type? ResponseType, IMessagePipelineBuilder? HandlerPipeline);
}

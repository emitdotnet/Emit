namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <inheritdoc />
public sealed class MessagePipelineBuilder : IMessagePipelineBuilder
{
    private readonly List<MiddlewareDescriptor> descriptors = [];

    /// <inheritdoc />
    public IReadOnlyList<MiddlewareDescriptor> Descriptors => descriptors;

    /// <inheritdoc />
    public IMessagePipelineBuilder Use(Type middlewareType, MiddlewareLifetime lifetime = default)
    {
        descriptors.Add(MiddlewareDescriptor.ForType(middlewareType, lifetime));
        return this;
    }

    /// <inheritdoc />
    public IMessagePipelineBuilder Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class
        => Use(typeof(TMiddleware), lifetime);

    /// <inheritdoc />
    public IMessagePipelineBuilder Use<TContext>(
        Func<IServiceProvider, IMiddleware<TContext>> factory,
        MiddlewareLifetime lifetime = default)
        where TContext : MessageContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        descriptors.Add(MiddlewareDescriptor.ForFactory(factory, lifetime));
        return this;
    }

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.MiddlewareType is null)
                continue;

            switch (descriptor.Lifetime)
            {
                case MiddlewareLifetime.Singleton:
                    services.TryAddSingleton(descriptor.MiddlewareType);
                    break;
                case MiddlewareLifetime.Scoped:
                    services.TryAddScoped(descriptor.MiddlewareType);
                    break;
            }
        }
    }

    /// <inheritdoc />
    public MessageDelegate<TContext> Build<TContext, TMessage>(
        IServiceProvider services,
        MessageDelegate<TContext> terminal,
        params IMessagePipelineBuilder[] parentLayers)
        where TContext : MessageContext<TMessage>
    {
        // Flatten: parents first (global → pattern), then this builder's (per-group)
        var allDescriptors = new List<MiddlewareDescriptor>();
        foreach (var parent in parentLayers)
            allDescriptors.AddRange(parent.Descriptors);
        allDescriptors.AddRange(Descriptors);

        // Compose the delegate chain in reverse order (last descriptor = closest to terminal)
        var next = terminal;
        var targetInterface = typeof(IMiddleware<TContext>);

        for (var i = allDescriptors.Count - 1; i >= 0; i--)
        {
            var descriptor = allDescriptors[i];
            var capturedNext = next;

            Func<IServiceProvider, IMiddleware<TContext>> resolve;

            if (descriptor.MiddlewareType is not null)
            {
                // Type-based: close open generics with TMessage
                var closedType = descriptor.MiddlewareType.IsGenericTypeDefinition
                    ? descriptor.MiddlewareType.MakeGenericType(typeof(TMessage))
                    : descriptor.MiddlewareType;

                if (!targetInterface.IsAssignableFrom(closedType))
                {
                    throw new InvalidOperationException(
                        $"Type '{closedType.Name}' does not implement IMiddleware<{typeof(TContext).Name}>.");
                }

                resolve = sp => (IMiddleware<TContext>)sp.GetRequiredService(closedType);
            }
            else
            {
                resolve = (Func<IServiceProvider, IMiddleware<TContext>>)descriptor.Factory!;
            }

            if (descriptor.Lifetime == MiddlewareLifetime.Singleton)
            {
                var instance = resolve(services);
                next = context => instance.InvokeAsync(context, capturedNext);
            }
            else
            {
                next = context => resolve(context.Services).InvokeAsync(context, capturedNext);
            }
        }

        return next;
    }
}

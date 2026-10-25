namespace Emit.Mediator;

using Emit.Abstractions;

/// <summary>
/// Context of a mediator request. Middleware written for <see cref="MediatorContext"/> runs on every request,
/// whatever its type.
/// </summary>
public abstract class MediatorContext : MessageContext;

/// <summary>
/// Context of a mediator request of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The request type.</typeparam>
public class MediatorContext<T> : MediatorContext
{
    /// <summary>
    /// The request being handled.
    /// </summary>
    public new required T Message
    {
        get => base.Message is T message ? message : default!;
        init => base.Message = value;
    }
}

namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Represents an asynchronous function that processes a pipeline context.
/// This is the fundamental building block of the middleware pipeline — each middleware
/// and the terminal handler are composed into a chain of these delegates.
/// Contravariant on <typeparamref name="TContext"/> so that a delegate accepting a base context
/// can be used where a more-derived context is expected.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
public delegate Task MessageDelegate<in TContext>(TContext context);

namespace Emit.Abstractions.Pipeline;

/// <summary>
/// A compiled middleware pipeline that processes a context of type <typeparamref name="TContext"/>.
/// Replaces <c>MessageDelegate&lt;TContext&gt;</c> as the fundamental pipeline abstraction.
/// Call <see cref="InvokeAsync"/> to execute the pipeline.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
public interface IMiddlewarePipeline<in TContext> where TContext : MessageContext
{
    /// <summary>
    /// Executes the pipeline for the given context.
    /// </summary>
    /// <param name="context">The context to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvokeAsync(TContext context);
}

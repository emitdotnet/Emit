namespace Emit.Abstractions.Pipeline;

/// <summary>
/// A middleware component of a message pipeline. The context type is the least the middleware needs, and it runs
/// on every pipeline whose context derives from it: <see cref="MessageContext"/> on every pipeline,
/// <see cref="ConsumeContext"/> on every consumer, <see cref="ConsumeContext{T}"/> on consumers of
/// <c>T</c>, and likewise for <see cref="SendContext"/> on producers.
/// </summary>
/// <typeparam name="TContext">The least derived context the middleware needs.</typeparam>
public interface IMiddleware<in TContext> where TContext : MessageContext
{
    /// <summary>
    /// Processes a message and optionally continues the pipeline.
    /// </summary>
    /// <param name="context">The context of the message flowing through the pipeline.</param>
    /// <param name="next">Continues the pipeline with the same context. Skip it to short-circuit.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvokeAsync(TContext context, Func<Task> next);
}

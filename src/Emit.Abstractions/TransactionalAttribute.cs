namespace Emit.Abstractions;

/// <summary>
/// Marks a consumer or mediator handler as transactional. When applied, the Emit pipeline
/// automatically wraps the handler invocation in a unit-of-work transaction. All outbox
/// entries produced within the handler are committed atomically with any business data writes.
/// Rollback occurs automatically on unhandled exceptions.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TransactionalAttribute : Attribute;

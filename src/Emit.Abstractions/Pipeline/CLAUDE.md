# Pipeline/

Middleware pipeline abstractions: the middleware interface, middleware levels, and builder contracts.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `IMiddleware.cs` | Contravariant middleware interface: the context type is the least the middleware needs, and it runs on every pipeline whose context derives from it; `next` continues the pipeline | Implement custom middleware |
| `IMessagePipelineBuilder.cs` | A middleware level for one kind of pipeline: `Use` by type, by factory, or a generic type definition closed per message type and checked on registration | Register middleware on a level |
| `MiddlewareLifetime.cs` | Enum controlling middleware instantiation: Singleton (once) or Scoped (per message) | Configure middleware lifetime |
| `IConsumerFilter.cs` | Interface for DI-injectable message filters that gate whether a message continues through the consumer pipeline | Implement a reusable consumer filter or understand the filter contract |
| `IConsumerGroupConfigurable.cs` | Generic interface extending IInboundConfigurable with group-level OnError and Validate | Configure consumer group error handling and validation |

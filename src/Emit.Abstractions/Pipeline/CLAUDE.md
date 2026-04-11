# Pipeline/

Middleware pipeline abstractions: delegates, middleware interfaces, and builder contracts.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MessageDelegate.cs` | Delegate type for async pipeline processing, contravariant on context type | Understand pipeline delegate composition |
| `MessagePipeline.cs` | Static utility composing middleware instances and terminal delegate into a single chain | Understand pipeline construction |
| `IMiddleware.cs` | Generic interface for middleware processing messages and delegating to next | Implement custom middleware |
| `IMiddlewarePipeline.cs` | Generic interface for pipeline nodes that process a context and delegate to the next node | Understand the pipeline node abstraction or implement pipeline adapters |
| `MessageMiddleware.cs` | Optional base class for cross-cutting middleware running on both inbound and outbound pipelines | Implement middleware that applies to all directions |
| `MiddlewareDescriptor.cs` | Descriptor capturing type or factory-based middleware registration with lifetime | Understand middleware registration model |
| `MiddlewareLifetime.cs` | Enum controlling middleware instantiation: Singleton (once) or Scoped (per message) | Configure middleware lifetime |
| `IConsumerFilter.cs` | Interface for DI-injectable message filters that gate whether a message continues through the consumer pipeline | Implement a reusable consumer filter or understand the filter contract |
| `IConfigurable.cs` | Interface for builders exposing middleware pipeline for Use extension methods | Extend pipeline configuration |
| `IMessagePipelineBuilder.cs` | Interface for collecting middleware descriptors and building typed MessageDelegate chains | Understand pipeline builder internals |
| `IConsumerGroupConfigurable.cs` | Generic interface extending IInboundConfigurable with group-level OnError and Validate | Configure consumer group error handling and validation |
| `IInboundConfigurable.cs` | Non-generic interface enabling uniform extension methods across inbound pipeline levels | Extend inbound pipeline configuration |
| `IOutboundConfigurable.cs` | Non-generic interface enabling uniform extension methods across outbound pipeline levels | Extend outbound pipeline configuration |

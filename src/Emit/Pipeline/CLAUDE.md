# Pipeline/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MessagePipelineBuilder.cs` | Concrete IMessagePipelineBuilder implementation registering middleware and building composed pipeline chains | Understand pipeline construction or modify builder behavior |
| `PipelineConfigurableExtensions.cs` | UseInbound, UseOutbound, and Use&lt;TMessage&gt; extension methods for middleware registration on pipeline-configurable builders | Add middleware via pipeline configuration API |
| `InboundFilterExtensions.cs` | Extension methods for registering consumer filters on inbound pipelines | Register consumer filters or understand filter registration |
| `ConsumerFilterMiddleware.cs` | Middleware invoking registered consumer filters in the inbound pipeline | Understand consumer filter invocation or debug filter execution |
| `ConsumerPipelineComposer.cs` | Composes a fully-layered inbound middleware pipeline for a single consumer or router entry | Understand pipeline composition order or debug middleware layering |
| `ConsumerPipelineEntry.cs` | Pairs consumer identity metadata with its composed pipeline delegate | Understand how consumer workers resolve and invoke pipelines |
| `HandlerInvoker.cs` | Terminal adapter resolving a consumer handler from the service provider and invoking it | Understand handler resolution or debug consumer invocation |
| `OutboxTerminalBuilder.cs` | Builds a terminal delegate that enqueues messages to the transactional outbox | Understand outbox enqueue flow or debug transaction/trace propagation |

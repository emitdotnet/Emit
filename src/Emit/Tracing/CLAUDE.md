# Tracing/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ActivityEnricherInvoker.cs` | Invoker calling registered activity enrichers for distributed tracing spans | Understand activity enrichment invocation or customize tracing behavior |
| `ActivityHelper.cs` | Helper utilities for creating and managing OpenTelemetry activities | Create custom activities or understand activity lifecycle |
| `ConsumeTracingMiddleware.cs` | Middleware creating distributed tracing spans for consumed messages | Understand consumer tracing or debug trace propagation |
| `EmitActivitySources.cs` | ActivitySource instances for Emit operations (produce, consume, outbox) | Reference activity sources or configure tracing collectors |
| `EmitTracingOptions.cs` | Configuration options for Emit distributed tracing | Configure tracing behavior, sampling, or enrichment |
| `EmitTracingOptionsValidator.cs` | IValidateOptions validator for EmitTracingOptions | Understand tracing configuration validation rules |
| `ProduceTracingMiddleware.cs` | Middleware creating distributed tracing spans for produced messages | Understand producer tracing or debug trace propagation |
| `OutboxActivityHelper.cs` | Helpers for creating and managing tracing activities during outbox processing | Understand outbox tracing or debug outbox activity creation |
| `TraceContextHeaderInjector.cs` | Injects W3C trace context (traceparent, tracestate, baggage) into message headers | Understand trace context propagation into message headers |

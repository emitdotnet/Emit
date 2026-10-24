# Tracing/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MessagingActivity.cs` | Starts every Emit span: `{operation} {destination}` names with the attributes samplers use, one rule for `Enabled` and `CreateRootActivities`, the creation context and baggage read from and written to headers, and failures recorded per the OpenTelemetry conventions | Start a span, change how Emit's spans start or record errors, or change how trace context crosses message boundaries |
| `ActivityNames.cs` | Operation names of Emit's spans: create, send, process, and the retry span | Reference span names |
| `ActivityTagNames.cs` | OpenTelemetry semantic convention attributes and Emit-specific tag names | Reference tag names for tracing configuration or instrumentation |
| `ActivityEnricherInvoker.cs` | Invoker calling registered activity enrichers for distributed tracing spans | Understand activity enrichment invocation or customize tracing behavior |
| `ConsumeTracingMiddleware.cs` | Outermost consume middleware starting the `process` span: parented on the message's creation context, or linked to each message's for a batch | Understand consumer tracing or debug trace propagation |
| `EmitActivitySources.cs` | The `Emit.Producer` and `Emit.Consumer` ActivitySource instances | Reference activity sources |
| `EmitTracingOptions.cs` | Configuration options for Emit distributed tracing | Configure tracing behavior, sampling, or enrichment |
| `EmitTracingOptionsValidator.cs` | IValidateOptions validator for EmitTracingOptions | Understand tracing configuration validation rules |
| `ProduceTracingMiddleware.cs` | Outbound middleware starting the `create` span, the message's creation context, and putting its trace context in the headers | Understand producer tracing or debug trace propagation |

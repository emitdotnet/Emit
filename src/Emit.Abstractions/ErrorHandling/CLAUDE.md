# ErrorHandling/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ErrorAction.cs` | Enum defining error handling actions: Consume (mark success), Retry (reprocess), or DeadLetter (send to DLQ) | Implement error handling logic or configure error policies |
| `ErrorClause.cs` | Record capturing a single error policy clause: exception type, optional predicate, and matched action | Understand the data model for a When&lt;TException&gt; clause in error policies |
| `ErrorPolicy.cs` | Immutable error policy built from ordered ErrorClause entries; evaluates exceptions to determine the matching action | Understand how error policies are evaluated at runtime |
| `ErrorPolicyActionBuilder.cs` | ErrorActionBuilder subclass adding Retry(maxAttempts, backoff) before a required terminal action | Understand how retry-then-deadletter/discard chains are constructed |
| `ErrorPolicyBuilder.cs` | Fluent builder for composing ordered exception clauses and a default action into an ErrorPolicy | Build or extend error policies with new clause types |

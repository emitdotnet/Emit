# Middleware/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `UnitOfWorkMiddleware.cs` | Bounds the unit of work of one consumer or handler: opens the transaction of a `[Transactional]` handler and runs the OnSucceeded/OnFailed callbacks once the outcome is final | Understand transactions, or how a nested handler joins an enclosing transaction |
| `UnitOfWorkCallbacks.cs` | Callbacks registered during one unit of work, current for the async flow | Understand how OnSucceeded and OnFailed reach the current unit of work |
| `InboxMiddleware.cs` | Directly inside the unit of work of a consumer or route that uses the inbox: records the message in its transaction and skips the consumer when already recorded | Understand or debug the inbox of single-message consumers |
| `BatchInboxMiddleware.cs` | Directly inside the unit of work of a batch consumer that uses the inbox: records the items in one call and removes those already recorded from the batch | Understand or debug the inbox of batch consumers |

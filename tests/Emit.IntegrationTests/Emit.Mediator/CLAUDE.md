# Emit.Mediator/

Mediator integration tests: request-response dispatch and exception propagation through the mediator pipeline.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MediatorIntegrationTests.cs` | End-to-end mediator tests: handler dispatch, response, and exception propagation via a real hosted service | Debug mediator integration test failures or understand mediator runtime behavior |
| `MediatorTransactionalHandlerTests.cs` | [Transactional] mediator handler tests: commit delivery, throw rollback, non-transactional pass-through with EF Core + Kafka | Debug mediator transactional handler tests or understand [Transactional] in mediator pipeline |

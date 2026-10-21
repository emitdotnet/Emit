# TestInfrastructure/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MongoDbContainerFixture.cs` | xUnit IAsyncLifetime fixture that starts a MongoDB Testcontainer with replica-set support and test commands (fail points), and exposes the connection string | Modify the test MongoDB setup, understand container lifecycle, or troubleshoot test infrastructure |

# TestInfrastructure/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MongoDbFailPointCollection.cs` | xUnit collection of the test classes that configure the server-wide `failCommand` fail point, so they run one at a time | Write a test that configures a fail point |
| `MongoDbContainerFixture.cs` | xUnit IAsyncLifetime fixture that starts a MongoDB Testcontainer with replica-set support and test commands (fail points), and exposes the connection string | Modify the test MongoDB setup, understand container lifecycle, or troubleshoot test infrastructure |

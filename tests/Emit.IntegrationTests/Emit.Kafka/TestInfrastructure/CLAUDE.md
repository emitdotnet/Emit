# TestInfrastructure/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaContainerFixture.cs` | xUnit IAsyncLifetime fixture that starts Kafka and Schema Registry Testcontainers and exposes bootstrap servers and schema registry URL | Modify Kafka test infrastructure, understand container lifecycle, or troubleshoot test connectivity |
| `CommitAwaiter.cs` | Completes on the first offset commit for the monitored topic | Wait for an offset commit in a test |
| `ToggleableTcpProxy.cs` | TCP proxy that can be cut off, resetting every connection, to make a dependency such as the schema registry unreachable | Simulate a dependency outage |

# Batch Consumer

A package sorting facility for Acme Logistics. Conveyor belt barcode scanners fire high-volume scan events into Kafka. A batch consumer detects mis-sorted packages and produces reroute commands through the transactional outbox. Everything is atomic — kill the app mid-batch and nothing is lost.

You don't need to do anything to see it work. A simulator fires realistic scan events automatically the moment you start the app.

## What Emit features you'll see

- **Batch consuming** — scan events are accumulated into batches (max 25 items, 1 s timeout) and processed together. Watch batch sizes change as the simulator cycles through different traffic volumes.
- **Transactional outbox** — reroute commands are enqueued atomically with MongoDB journey writes. The outbox daemon delivers them to Kafka.
- **Per-item validation** — FluentValidation rejects scans with empty barcodes, negative weights, or unknown facilities. Invalid items are dead-lettered individually without affecting the rest of the batch.
- **Rate limiting** — a token bucket (80 permits/sec, burst 50) throttles consumption during the simulator's burst phase (~150 scans/sec). Watch backpressure build in the logs.
- **Circuit breaker** — stop the MongoDB container and the consumer group pauses for 15 seconds. Restart it and processing resumes automatically.
- **Retry with exponential backoff** — transient failures retry the entire batch up to 3 times with exponential delay starting at 200 ms.
- **Dead-letter topic** — permanently failed or invalid scans land in `sorting.scans.dlt`.
- **Auto-provisioning** — all required Kafka topics are created at startup.
- **Health checks** — `/health` endpoint reports Kafka and MongoDB health.
- **OpenTelemetry** — Prometheus metrics and Tempo traces, explored directly in Grafana.

## Running it

[Start the infrastructure](../README.md#starting-infrastructure) first, then:

```bash
dotnet run --project BatchConsumer
```

The app listens on `http://localhost:5002`. The simulator kicks in 5 seconds after startup.

## What to watch for

**In the console**, you'll see the simulator cycling through three phases:

```
[Simulator] Phase: Normal (30s, ~30/sec)
[Simulator] Phase: Burst (8s, ~142/sec)
[Simulator] Phase: Quiet (15s, ~5/sec)
```

The batch consumer logs every batch it processes:

```
Processing batch of 25 scans
Mis-sorted: PKG-04821 on lane 3, should be lane 1 (zip 1847)
Batch complete: 25 scans, 2 reroutes, 14 ms
```

During **Burst**, batches fill to the 25-item maximum. During **Quiet**, batches are smaller and trigger on the 1-second timeout. During **Normal**, you'll see a mix.

**In the [observability tools](../README.md#observability-tools):**

- **Grafana** — watch consume throughput spike during burst phases and the rate limiter kick in
- **Grafana > Drilldown > Traces** — search for `BatchConsumer` and see batch processing spans including individual reroute produces
- **Kafka UI** — inspect `sorting.scans`, `sorting.reroutes`, and `sorting.scans.dlt` topics and consumer lag
- **Mongo Express** — browse the `batch_consumer` database with `package_journeys` and `emit_outbox` collections

## Simulator phases

| Phase | Duration | Rate | What it demonstrates |
|-------|----------|------|---------------------|
| Normal | 30 s | ~30/sec | Partial and full batches in steady state |
| Burst | 8 s | ~150/sec | Batches fill to MaxSize=25, rate limiter throttles |
| Quiet | 15 s | ~5/sec | Small batches dispatched on 1 s timeout |

The simulator also injects faults: ~3% empty barcodes, ~2% negative weights, ~1% unknown facilities (all caught by validation), and ~8% wrong-lane scans (valid data that triggers reroute commands).

## Trying the circuit breaker

While the app is running:

```bash
docker stop samples-mongo-1     # circuit breaker opens, consumer pauses 15 s
docker start samples-mongo-1    # circuit breaker closes, processing resumes
```

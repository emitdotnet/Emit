# Building Sentinel

A smart building access hub for Acme Corp HQ. Badge readers, motion sensors, and door controllers fire events at an API. Those events hit the transactional outbox, flow through Kafka, and get processed by two independent consumer groups. One group watches for suspicious badge activity and raises alarms. The other tracks every device's heartbeat.

You don't need to do anything to see it work. A simulator fires realistic events automatically the moment you start the app.

## What Emit features you'll see

- **Transactional outbox** — the event is saved to the database and enqueued to Kafka in one atomic transaction. Kill the app mid-flight; nothing is lost.
- **Mediator** — the API endpoint dispatches a command and stays out of everything else.
- **Auto-provisioning** — all required Kafka topics are created at startup. No manual topic creation needed.
- **Dead-letter topic** — permanently failed messages are routed to `building.events.dlt` after retries are exhausted.
- **Kafka router consumer** (`building.classifier`) — routes on `eventType`. Only `access.denied` events are handled; everything else is discarded. This is the point of the router.
- **Kafka simple consumer** (`building.watchdog`) — handles every single event, upserts a heartbeat per device. Total contrast to the router above.
- **FluentValidation** — both consumer groups validate incoming messages. The simulator intentionally produces ~5% invalid events to demonstrate validation failures and dead-lettering.
- **Health checks** — `/health` endpoint reports Kafka and database health.
- **OpenTelemetry** — Prometheus metrics and Tempo traces, explored directly in Grafana.

## Running it

[Start the infrastructure](../README.md#starting-infrastructure) first, then pick a persistence backend:

```bash
# MongoDB
dotnet run --project BuildingSentinel.MongoDB

# PostgreSQL
dotnet run --project BuildingSentinel.PostgreSQL
```

The API is available at `http://localhost:5000` (Scalar docs at `http://localhost:5000/scalar/v1`). The simulator kicks in 5 seconds after startup.

## What to watch for

**In the console**, you'll see a stream of events:

```
[Simulator] Normal access — badge emp-017 granted at Floor 5 - East Wing
[Simulator] Suspect attempt — badge badge-suspect-01 denied at Server Room (Main) — total suspect denials: 2
[RUSH] Morning rush simulation started
```

Keep an eye out for the alarm line, it's the payoff:

```
ALARM: device badge-suspect-01 at Server Room (Main) has 3 consecutive access denials
```

That's `AccessDeniedConsumer` inside the `building.classifier` group firing after the suspect badge crosses the threshold. Every ~10 seconds the suspect tries again, so it happens pretty fast.

**Every 20 events**, a morning rush plays out: 8 rapid lobby events at 150 ms each. Watch the throughput spike in Grafana.

**In the [observability tools](../README.md#observability-tools):**

- **Grafana** — watch the outbox throughput spike during morning rushes
- **Grafana > Drilldown > Traces** — search for `BuildingSentinel` and watch a single HTTP request fan out into a DB write, an outbox enqueue, and a Kafka consume, all in one trace
- **Kafka UI** — inspect `building.events` and watch `building.classifier` / `building.watchdog` consumer lag
- **Mongo Express / pgAdmin** — browse the `building_events`, `access_denial_alerts`, and `device_heartbeats` collections

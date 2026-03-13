# Emit Samples

Each sample is a self-contained project that demonstrates real-world usage of Emit. Every sample ships with a `docker-compose.yml` that starts all required infrastructure and observability tooling — no environment variables, no manual setup.

## Samples

| Sample | What it demonstrates |
|---|---|
| [building-sentinel](building-sentinel/) | Transactional outbox, mediator, Kafka router consumer, multiple consumer groups, auto-provisioning, dead-letter topic, FluentValidation, health checks, OpenTelemetry metrics and tracing |
| [distributed-locks](distributed-locks/) | Distributed locking with contention, lock TTL, dual persistence (MongoDB/PostgreSQL), OpenTelemetry lock metrics |

---

## building-sentinel

A smart building access and security hub. Physical devices (badge readers, motion sensors, alarm panels) submit events to a REST API. The API records every event transactionally and publishes it to Kafka. Two independent consumer groups react to the stream.

### What is demonstrated

**Transactional outbox**
The command handler writes the raw device event to the database and enqueues the Kafka message in a single transaction. Kafka delivery is guaranteed even if the broker is temporarily unavailable.

**Mediator**
Each API endpoint dispatches a command through `IMediator`. The handler validates, persists, and enqueues — nothing else.

**Auto-provisioning**
`kafka.AutoProvision()` creates all required Kafka topics at startup. No manual topic creation or admin scripts needed.

**Dead-letter topic**
`kafka.DeadLetter("building.events.dlt")` configures a dead-letter topic. Messages that fail after retries are automatically routed there with diagnostic headers.

**Kafka router consumer — `building.classifier`**
Consumes `building.events` and routes on the `eventType` field. Only `access.denied` events are handled — they are aggregated per badge and an alarm flag is raised when the denial count crosses a threshold. All other event types are ignored. This is the canonical demonstration of the router: one consumer group, one topic, selective handling by event type.

**Kafka simple consumer — `building.watchdog`**
Consumes every event regardless of type and upserts a heartbeat record per device (`last_seen_at`, `event_count`). Exposes a `/api/devices/status` endpoint that surfaces devices that have gone silent. The sharpest contrast with the router consumer — it never looks at `eventType`.

**FluentValidation**
Both consumer groups validate incoming `BuildingEvent` messages using `Emit.FluentValidation`. A `BuildingEventValidator` enforces that every event has a non-empty `DeviceId`, a non-empty `Location`, and a known `EventType`. Messages that fail validation are dead-lettered — open the `building.events.dlt` topic in Kafka UI to inspect rejected events. The simulator intentionally produces ~5% "device glitch" events with an empty `Location` to demonstrate validation failures in action.

**Health checks**
`/health` endpoint reports Kafka broker connectivity and database health (MongoDB or PostgreSQL depending on the startup project).

**OpenTelemetry**
Metrics exported via Prometheus and visualised in Grafana. Distributed traces collected by Tempo and explored in Grafana.

### Persistence

The sample runs with either **MongoDB** or **PostgreSQL**. All application logic lives in `BuildingSentinel.Common`. The startup project selects the persistence backend — nothing else changes.

| Startup project | Backend |
|---|---|
| `BuildingSentinel.MongoDB` | MongoDB |
| `BuildingSentinel.PostgreSQL` | PostgreSQL via EF Core + Npgsql |

---

## Running a sample

### 1. Start infrastructure

From the `samples/` directory:

```bash
docker compose up -d
```

This starts Kafka, MongoDB, PostgreSQL, Schema Registry, Prometheus, Grafana, Tempo, pgAdmin, and Mongo Express. All services are pre-configured and ready immediately.

### 2. Run the application

Pick a persistence backend:

```bash
# MongoDB
dotnet run --project BuildingSentinel.MongoDB

# PostgreSQL
dotnet run --project BuildingSentinel.PostgreSQL
```

The API is available at `http://localhost:5000`. A built-in simulator starts automatically and begins sending realistic building events after a 5-second warm-up — no manual input required. Watch the console logs to see events flowing through the system.

### 3. Explore

| Tool | URL | Purpose |
|---|---|---|
| Scalar API reference | http://localhost:5000/scalar/v1 | Submit device events, query device status |
| Grafana | http://localhost:3100 | Emit metrics dashboards (no login required) |
| Grafana Explore | http://localhost:3100/explore | Distributed traces (Drilldown → Traces) |
| Mongo Express | http://localhost:8082 | Browse MongoDB collections |
| pgAdmin | http://localhost:5050 | Browse PostgreSQL tables |
| Kafka UI | http://localhost:8080 | Inspect topics, consumer group lag, messages |

### 4. Stop infrastructure

```bash
docker compose down
```

Add `-v` to also remove all persisted data volumes.

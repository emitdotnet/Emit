# Emit Samples

Self-contained projects demonstrating real-world Emit usage. Every sample supports both **MongoDB** and **PostgreSQL**.

| Sample | What it demonstrates |
|---|---|
| [building-sentinel](building-sentinel/) | Transactional outbox, mediator, Kafka consumers, dead-letter topics, FluentValidation, health checks, OpenTelemetry |
| [distributed-locks](distributed-locks/) | Distributed locking with contention, lock TTL, OpenTelemetry lock metrics |

## Prerequisites

- [.NET 10+ SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/)

## Starting infrastructure

All samples share the same infrastructure. From the `samples/` directory:

```bash
docker compose up -d
```

This starts Kafka, MongoDB, PostgreSQL, Schema Registry, Prometheus, Grafana, Tempo, pgAdmin, and Mongo Express. Give MongoDB ~20 seconds to elect its replica-set primary before running a sample.

Then follow the **Running it** section in the sample's own README.

## Observability tools

These URLs are available as soon as `docker compose up` finishes:

| Tool | URL | What to look at |
|---|---|---|
| Grafana | http://localhost:3100 | Emit metrics dashboards (no login required) |
| Grafana Explore | http://localhost:3100/explore | Distributed traces (Drilldown > Traces) |
| Kafka UI | http://localhost:8080 | Topics, consumer group lag, messages |
| Mongo Express | http://localhost:8082 | MongoDB collections |
| pgAdmin | http://localhost:5050 | PostgreSQL tables |

## Stopping infrastructure

```bash
docker compose down        # keep data volumes
docker compose down -v     # remove everything
```

# samples/

Runnable sample applications demonstrating real-world Emit usage patterns.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Sample catalog, infrastructure startup instructions, and tool URLs | Run a sample or understand what each sample demonstrates |
| `docker-compose.yml` | Docker Compose file starting Kafka, MongoDB, PostgreSQL, Prometheus, Grafana, Tempo, and supporting tools | Start sample infrastructure or understand service configuration |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `building-sentinel/` | Smart building access and security hub demonstrating transactional outbox, mediator, Kafka router/simple consumers, and OpenTelemetry | Explore a complete end-to-end Emit integration or use as a reference implementation |
| `docker/` | Grafana dashboards, Prometheus config, MongoDB replica-set init, and pgAdmin server config | Customize observability dashboards or troubleshoot infrastructure configuration |

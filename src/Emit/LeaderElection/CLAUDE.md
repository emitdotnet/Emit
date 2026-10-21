# LeaderElection/

Leader election implementation: periodic heartbeat worker that races for leadership via persistence.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `HeartbeatWorker.cs` | BackgroundService that sends periodic heartbeats for its service, tracks lease validity locally, fences its daemons when the lease would expire (renewed only once the node read its assignments), declares no daemons once its lease ran out, claims the lease only while it observes its service, runs daemon coordination within the heartbeat interval, and resigns only leases its own heartbeats wrote, once the dropped leadership token reads cancelled | Modify heartbeat interval, leader election timing, or understand leader state transitions |
| `LeaseTokenSource.cs` | Cancellation source that cancels itself when a lease runs out, checked periodically and on read so a suspend is noticed; its callbacks run on a callback scheduler, so one that blocks or throws cannot hold up or stop the node | Change how leadership or fencing tokens are cancelled |
| `ClockReading.cs` | An instant on both the monotonic and the wall clock; time since it is the longer of the two, so neither a clock step nor a suspend extends a lease. Only for this node's own deadlines: waits on other nodes use the monotonic clock alone | Measure this node's lease or heartbeat gap |

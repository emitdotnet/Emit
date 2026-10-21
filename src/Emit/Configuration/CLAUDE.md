# Configuration/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `OutboxOptions.cs` | Outbox worker configuration: polling interval, batch size, max groups per cycle, lock duration, and lock renewal interval | Configure outbox worker behavior |
| `OutboxOptionsValidator.cs` | IValidateOptions validator enforcing constraints on all outbox options | Modify validation rules for outbox options |
| `ValidationConstants.cs` | Shared constants (min/max values) used across option validators | Adjust validation boundaries |
| `DaemonOptions.cs` | Daemon configuration: acknowledge timeout, drain timeout, and the failure limit and cooldown after which a node stops offering a daemon | Configure daemon worker behavior |
| `DaemonOptionsValidator.cs` | IValidateOptions validator enforcing constraints on daemon options | Modify validation rules for daemon options |
| `LeaderElectionOptions.cs` | Leader election configuration: service name, heartbeat interval, lease duration, query timeout, and TTL | Configure leader election behavior |
| `LeaderElectionOptionsDefaults.cs` | Post-configure step defaulting ServiceName to the host application name or entry assembly name | Understand how the service name is resolved |
| `LeaderElectionOptionsValidator.cs` | IValidateOptions validator enforcing timing constraints, service name rules (set, at most 200 characters) and the instance id length | Modify validation rules for leader election options |

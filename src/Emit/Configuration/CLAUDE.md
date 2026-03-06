# Configuration/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `OutboxOptions.cs` | Outbox worker configuration: polling interval, batch size, max groups per cycle, lock duration, and lock renewal interval | Configure outbox worker behavior |
| `OutboxOptionsValidator.cs` | IValidateOptions validator enforcing constraints on all outbox options | Modify validation rules for outbox options |
| `ValidationConstants.cs` | Shared constants (min/max values) used across option validators | Adjust validation boundaries |
| `DaemonOptions.cs` | Daemon worker configuration: acknowledge timeout and drain timeout | Configure daemon worker behavior |
| `DaemonOptionsValidator.cs` | IValidateOptions validator enforcing constraints on daemon options | Modify validation rules for daemon options |
| `LeaderElectionOptions.cs` | Leader election configuration: heartbeat interval, lease duration, query timeout, and TTL | Configure leader election behavior |
| `LeaderElectionOptionsValidator.cs` | IValidateOptions validator enforcing constraints on leader election options | Modify validation rules for leader election options |

# Configuration/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `LockCleanupOptions.cs` | Options for expired lock cleanup interval and batch size | Configure lock cleanup behavior |
| `LockCleanupOptionsValidator.cs` | IValidateOptions validator enforcing minimum values for lock cleanup settings | Modify validation rules for lock cleanup options |
| `EntityFrameworkCoreInboxOptions.cs` | Inbox options: Retention (7 days), CleanupInterval (1 minute), CleanupBatchSize (1000) | Configure the EF Core inbox |
| `EntityFrameworkCoreInboxOptionsValidator.cs` | IValidateOptions validator requiring every inbox option to be greater than zero | Modify validation rules for inbox options |

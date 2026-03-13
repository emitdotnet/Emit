# Emit.FluentValidation/

FluentValidation integration: adapter bridging IValidator<T> into the Emit validation pipeline.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `FluentValidationMessageValidator.cs` | Adapter implementing IMessageValidator<T> by delegating to FluentValidation's IValidator<T> | Understand how FluentValidation validators are bridged into Emit |
| `FluentValidationExtensions.cs` | ValidateWithFluentValidation extension method on IConsumerGroupConfigurable<T> | Register FluentValidation validators on consumer groups |
| `README.md` | Package overview and usage | Understand what this package provides |
| `Emit.FluentValidation.csproj` | Project file with FluentValidation dependency | Configure FluentValidation package dependencies |

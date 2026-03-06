# Contributing to Emit

Thank you for your interest in contributing. This guide covers how to submit changes and what to expect during review. For build commands and setup instructions, see the [documentation](https://emitdotnet.github.io/Emit).

## Submitting Changes

1. **Open an issue first** for anything beyond a trivial fix. This prevents wasted effort on changes that may not align with the project direction.
2. Fork the repository and create a branch from `main`.
3. Make your changes. Include tests for new functionality.
4. Run `dotnet format` and `dotnet test` locally.
5. Open a pull request against `main`.

### Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

- `fix:` for bug fixes
- `feat:` for new features
- `docs:` for documentation changes
- `refactor:` for code changes that neither fix bugs nor add features
- `test:` for test additions or corrections
- `chore:` for build, CI, or tooling changes

### What Makes a Good PR

- Focused on a single change
- Tests pass (`dotnet test`)
- Formatting is clean (`dotnet format`)
- Documentation updated if behavior changes
- Breaking changes are clearly documented in the PR description

## Code Style

- File-scoped namespaces
- Latest C# features (primary constructors, collection expressions, pattern matching)
- `ConfigureAwait(false)` in all library code
- XML docs on public APIs
- See [`.editorconfig`](.editorconfig) for the full ruleset

## Testing

Emit uses abstract compliance test bases (e.g., `OutboxRepositoryCompliance`, `DistributedLockCompliance`) that define shared behavior once. Each persistence provider inherits from these bases to prove correctness without duplicating tests. If you add a new provider or change shared behavior, implement or update the corresponding compliance class rather than writing one-off tests per provider.

CI runs both unit tests and integration tests. Integration tests require Docker and start all infrastructure automatically via [Testcontainers](https://testcontainers.com/).

## What to Work On

Look for issues labeled [`good first issue`](https://github.com/emitdotnet/Emit/labels/good%20first%20issue) or [`help wanted`](https://github.com/emitdotnet/Emit/labels/help%20wanted).

For larger changes or new features, please open an issue to discuss the approach before starting work.

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold these standards.

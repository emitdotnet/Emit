# workflows/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ci.yml` | CI workflow: unit tests, format check, integration tests with MongoDB/PostgreSQL containers. Skipped for docs-only changes | Configure CI pipeline or troubleshoot build failures |
| `docs.yml` | Builds the docs site on changes under `docs/` | Troubleshoot docs build failures or docs dependency PRs |
| `deploy-docs.yml` | Manual GitHub Pages deploy of the docs site | Publish docs outside a release |
| `publish.yml` | Tag-triggered NuGet publish workflow with version extraction and package push | Configure release pipeline or troubleshoot publishing |

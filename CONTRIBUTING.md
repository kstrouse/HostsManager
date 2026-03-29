# Contributing

Contributions are welcome.

## Development Setup

- Install the .NET 10 SDK
- Clone the repository
- Build the solution:

```powershell
dotnet build HostsManager.slnx
```

- Run the tests:

```powershell
dotnet test HostsManager.slnx
```

## Project Expectations

- Keep changes scoped and reviewable
- Add direct unit tests for new services and behavior-heavy classes
- Prefer preserving existing UI and behavior unless the change is intentionally user-facing
- Do not mix unrelated cleanup into the same change set

## Pull Requests

Before opening a PR:

- ensure the solution builds cleanly
- ensure the full test suite passes
- update documentation when behavior, setup, or release steps change
- include a concise explanation of what changed and why

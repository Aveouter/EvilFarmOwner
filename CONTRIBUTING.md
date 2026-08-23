# Contributing

This project uses issue-first development. Every meaningful change should start from an issue and land through a pull request.

## Workflow

1. Open or choose an issue.
2. Confirm the issue has type, priority, area, and status labels.
3. Create a branch from `main`.
4. Make a focused change.
5. Open a pull request using the repository PR template.
6. Keep the PR scoped to one issue or one tightly related set of changes.

## Commit Style

Use Conventional Commits:

```text
feat(ui): add hiring menu
fix(multiplayer): block client-side work passes
docs(readme): clarify installation
chore(project): add issue templates
refactor(tasks): split farm work handlers
```

## Branch Naming

Use short branch names:

```text
feat/hiring-menu
fix/host-only-work
docs/readme-install
chore/project-templates
```

## Pull Request Rules

- Do not push directly to `main`.
- Link the issue the PR resolves.
- Keep author and committer identity aligned with the repository owner when working through this repository.
- Include validation notes, even for documentation changes.
- Mention multiplayer impact for gameplay changes.

## Local Validation

For code changes, run:

```bash
dotnet build -c Release
dotnet run -c Release --project tests/EvilFarmOwner.LogicTests.csproj
```

For gameplay changes, also test through SMAPI in a disposable save.

For a release candidate, run the complete deterministic build and package allowlist check:

```bash
./scripts/verify-release.sh
```

This command does not deploy into the live game. Real single-player and remote host/farmhand acceptance tests are still required for gameplay and multiplayer releases.

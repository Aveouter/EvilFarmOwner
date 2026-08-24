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

The release verifier requires a clean Git worktree and prints the exact source commit and tree associated with its artifact hash. For a pre-commit diagnostic only, a developer may run `EFO_RELEASE_ALLOW_DIRTY=1 ./scripts/verify-release.sh`; hashes from that override are not release evidence.

GitHub Actions runs repository-only source checks for shell syntax, whitespace, JSON and manifest metadata, version alignment, and English/Chinese translation-key parity. It intentionally does not claim to compile the mod, because the hosted runner does not contain the proprietary game assemblies. The clean local release build and gameplay acceptance gates remain mandatory.

This command does not deploy into the live game. Real single-player and remote host/farmhand acceptance tests are still required for gameplay and multiplayer releases.

Issue #42 storage failure testing uses the compile-gated procedure in
[`docs/STORAGE_FAULT_ACCEPTANCE.md`](docs/STORAGE_FAULT_ACCEPTANCE.md). The
instrumented DLL is test-only; rebuild without `EnableAcceptanceFaults` and run
the clean release verifier before recording or distributing a candidate.

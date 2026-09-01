# Issue Tracker: GitHub

Issues, implementation tickets, and PRDs for this repo live in GitHub Issues on `Aveouter/EvilFarmOwner`.

Use the `gh` CLI for issue operations from inside this repository. The repository is inferred from `git remote -v`.

## Conventions

- Create an issue with `gh issue create --title "..." --body "..."`.
- Read an issue with `gh issue view <number> --comments`.
- List issues with `gh issue list --state open --json number,title,body,labels,comments`.
- Comment on an issue with `gh issue comment <number> --body "..."`.
- Apply labels with `gh issue edit <number> --add-label "..."`.
- Remove labels with `gh issue edit <number> --remove-label "..."`.
- Close issues with `gh issue close <number> --comment "..."`.

## Skill Routing

When a skill says "publish to the issue tracker", create a GitHub issue.

When a skill says "fetch the relevant ticket", read the GitHub issue and comments.

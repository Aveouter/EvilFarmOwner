# Triage Labels

The engineering skills use five canonical triage roles. This file maps those roles to the labels configured in GitHub Issues.

| Skill role | Repo label | Meaning |
| --- | --- | --- |
| `needs-triage` | `status:needs-triage` | Maintainer needs to evaluate the issue. |
| `needs-info` | `status:needs-info` | Waiting on the reporter or user for missing details. |
| `ready-for-agent` | `status:ready-for-agent` | Fully specified and ready for Codex implementation. |
| `ready-for-human` | `status:ready-for-human` | Requires human implementation, product judgment, or live validation. |
| `wontfix` | `wontfix` | Will not be actioned. |

When a skill mentions a canonical role, apply the corresponding repo label from this table.

The repo also uses type, priority, and area labels. New implementation work should keep using the established pattern:

- `type:*`
- `priority:*`
- `area:*`
- one `status:*` label

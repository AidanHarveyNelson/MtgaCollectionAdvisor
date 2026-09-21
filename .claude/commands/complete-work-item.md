# Complete Work Item

Push a finished feature branch, open a pull request against `master`, and give the reviewer
enough context to review without reading raw diffs.

**Issue to complete:** `$ARGUMENTS`

---

## Step 1 — Resolve input

`ISSUE_NUMBER` and `REPO_SLUG` from `$ARGUMENTS` (bare number or URL; slug from
`git remote get-url origin`). Empty → ask and stop.

---

## Step 2 — Verify the branch

```bash
git branch --show-current
git status --short
```

- Branch must start with `feature/ISSUE_NUMBER`. If not, stop and say which branch is active.
- Uncommitted changes → stop and ask the user to commit or stash.

---

## Step 3 — Fetch the QA report

```bash
gh issue view ISSUE_NUMBER --repo REPO_SLUG --json number,title,comments
```

Find the comment starting with `<!-- QA-REPORT -->`. If there is none, stop and say to run
`/implement-work-item ISSUE_NUMBER` first.

Keep these sections to paste into the PR: What Was Built, Files Created / Modified, Build
Status, Unit Test Results, App Verification, Manual Testing Required, AC Completion Status,
Deviations, Open Questions, Follow-up Issues.

---

## Step 4 — Update CLAUDE.md, if this taught us something

Read the QA report's Deviations and What Was Built. Ask: did this work reveal a constraint
or pattern that the next person — or the next session — would otherwise rediscover the hard
way?

Worth recording:
- Framework behaviour that silently does the wrong thing (the kind that produces no error).
- A rule about where logic must live to stay testable.
- Anything about the MTGA client's memory layout or the external APIs' quirks.

Not worth recording: one-off tactical choices, anything already in `CLAUDE.md`, ordinary
framework boilerplate.

If there is something, add it to the relevant section of `CLAUDE.md` — one rule per
paragraph, concise — and commit:

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with learnings from #ISSUE_NUMBER"
```

If there is nothing, skip this step. Do not commit an empty change to say so.

---

## Step 5 — Push

```bash
git push -u origin HEAD
```

If the remote branch diverged, stop and report the error. Never force-push.

---

## Step 6 — Open the PR

Write the body to `pr_body_ISSUE_NUMBER.md` in the repo root (Write tool, absolute Windows
path — never `/tmp/`), then:

```bash
gh pr create --title "feat(#ISSUE_NUMBER): [issue title]" \
  --body-file "pr_body_ISSUE_NUMBER.md" --base master --repo REPO_SLUG
rm -f pr_body_ISSUE_NUMBER.md
```

Body:

```markdown
## Summary

Closes #ISSUE_NUMBER

[What Was Built, verbatim.]

## What Changed
[Files table.]

## Test Results
[Test table + total. State the Release build result.]

## Acceptance Criteria
[AC checklist.]

## Deviations
[Or "None."]

## Follow-up Issues
[Or "None."]

## ⚠️ Manual Testing Required
> Lead with what automation could not reach and why.
[Numbered steps with expected results.]

## Open Questions
[Or omit.]
```

If a PR already exists for the branch, fetch its URL with
`gh pr view --repo REPO_SLUG --json url --jq '.url'` and continue with it.

---

## Step 7 — Request review

```bash
gh pr comment PR_NUMBER --repo REPO_SLUG --body "..."
```

Write a checklist that fits *this* change. Generic checklists get ticked without being read.
Items that usually apply here:

- Logic that could be tested without a UI lives in `Core`, not in a `.razor` file
- The Manual Testing steps were actually executed against a running app
- Existing behaviour touched by a refactor still works
- User input reaching SQL is escaped

Only include an item if this PR could plausibly violate it.

---

## Step 8 — Report

Print the PR URL, the branch name, and:
"PR #N created for issue #M — waiting for human review."

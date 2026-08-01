# Commit Workflow

## Why

Each commit should explain one coherent change and remain safe to review or revert independently.

## Format

Use Conventional Commits:

```text
<type>(<optional-scope>): <imperative summary>
```

Examples:

```text
feat(auth): add session timeout
fix(api): handle empty upstream response
docs: document local test setup
```

Use the same types defined in [`branch.md`](branch.md).

## Rules

- Keep subject at 50 characters or fewer when practical.
- Use imperative present tense.
- Do not end subject with a period.
- Describe why in body when motivation is not obvious.
- Reference issue in footer when repository automation requires it.
- Do not mix unrelated behavior, formatting, and refactoring.
- Do not commit secrets, generated noise, or local-only configuration.

## Splitting

Split work into one commit per feature unit. A branch that implements several
capabilities produces several commits, not one large one.

- A commit adds one capability, one fix, or one mechanical change. If the subject
  needs "and" to describe it, it is two commits.
- Every commit must build. A commit that does not compile is not a valid split
  point; merge it with its neighbour instead. Broken intermediate commits make
  bisecting useless, which is most of the value of splitting.
- When one file carries changes belonging to different commits, stage by hunk.
  Falling back to one-commit-per-file lumps unrelated concerns together.
- Order commits so each one stands on its own: shared state and types first, then
  the features that use them, then tests.
- Splitting an existing large commit is history rewriting only. Verify the final
  tree is identical to the original before force-updating a branch.

## Body

Add a body when change has non-obvious constraints or tradeoffs:

```text
fix(cache): preserve stale values during refresh

Concurrent refreshes previously cleared readable values. Keep stale data
until replacement succeeds so callers retain deterministic fallback behavior.

Refs #123
```

# Quarry vNEXT

_Unreleased — staging area for the next tagged release_

> **Convention.** This file accumulates per-PR release notes between tags. When `llm-release.md` runs, its content is merged into the new `release-notes-vX.Y.Z.md`, this file is deleted, and the deletion is staged in the same `Release vX.Y.Z` commit. PRs that need a release-notes entry append to the appropriate section below; PRs that don't need an entry leave this file untouched. The file uses the same section structure as `release-notes-vX.Y.Z.md` (see the Appendix in `llm-release.md`).

---

## Highlights

<!-- 8–10 short bullets summarizing the release; bold the feature name. -->

- **DISTINCT + ORDER BY on a non-projected column now portable across all four dialects** — chains like `OrderBy(non-projected).Distinct().Select(proj)` previously failed at runtime on PostgreSQL (42P10) and SQL Server, and silently produced implementation-defined results on SQLite/MySQL. The generator now emits a derived-table wrap on all dialects so the construct has standard, deterministic semantics everywhere.

---

## Breaking Changes

### Behavior Changes

- **`OrderBy(non-projected).Distinct().Select(proj)` row count changes on SQLite and MySQL** (#267, closes #267). The flat shape SQLite/MySQL accepted produced one row per `proj` with an arbitrary `non-projected` value picked by the engine. The new wrap applies DISTINCT to `(proj, non-projected)`, so chains of this exact shape now return one row per `(proj, non-projected)` pair on every dialect. PostgreSQL and SQL Server users were already affected (PG returned 42P10; SS rejected under standard rules), so this only surfaces as a row-count change for SQLite/MySQL users on the affected query shape. The previous behavior was implementation-defined leniency, not a documented contract.

  **Before:** SQLite/MySQL might return 2 rows for `db.Users().Join<Order>(…).OrderBy((u,o) => o.Total).Distinct().Select((u,o) => u.UserName)` against a seed where one user has multiple orders. Which `o.Total` got picked for ordering was unspecified.

  **After:** All four dialects return one row per `(UserName, Total)` pair (e.g., 3 rows for the same seed) with a deterministic order. To preserve the old behavior, project the `OrderBy` column explicitly and dedupe in C#, or rewrite to use `GroupBy` + an aggregate.

---

## Bug Fixes

### SQL Correctness

- **Generator emits non-portable `SELECT DISTINCT … ORDER BY <non-projected>` on PostgreSQL** (#267, closes #267). PostgreSQL rejected the flat shape with `42P10: for SELECT DISTINCT, ORDER BY expressions must appear in select list`; SQL Server rejected the same construct under standard rules. The generator now wraps such chains in a derived table — inner `SELECT DISTINCT` carries the original projection plus any non-projected ORDER BY columns under aliases (`_o0`, `_o1`, …), and the outer SELECT projects the original columns ordered by those inner aliases with pagination applied to the outer. Detection compares each ORDER BY expression's rendered SQL against the projection-column reference set; if every term matches, the flat form is preserved. Set operations are excluded — the existing post-union derived-table wrap handles those paths.

---

## Stats

- 1 PR merged since v0.3.2

---

## Full Changelog

### Bug Fixes

- Fix DISTINCT + ORDER BY non-projected column emits subquery wrap (#267)

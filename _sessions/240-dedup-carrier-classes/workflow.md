# Workflow: 240-dedup-carrier-classes
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #240
pr:
session: 1
phases-total: 3
phases-complete: 1
## Problem Statement
Structurally identical carriers produce separate classes, causing 30-50% code bloat in generated output. Carriers with the same parameter types, field layout, and interface set should share a single class definition.

Baseline: All 3,190 tests pass (175 Migration + 103 Analyzers + 2,912 Quarry.Tests). No pre-existing failures.

## Decisions
- 2026-04-10: Full structural key for dedup — include Fields, MaskType/MaskBitCount, ExtractionPlan extractors, SqlVariants, ReaderDelegateCode, collection flags, and resolved interfaces. Guarantees correctness (class text identical before merging).
- 2026-04-10: Structural key type lives as a private helper inside FileEmitter (co-located with emission logic).
- 2026-04-10: Overhead acceptable — hashing cost is negligible vs. the emission work it eliminates. No special allocation constraints needed.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Loaded issue #240, created worktree, baseline green |
| 1 | DESIGN | PLAN | Full structural key approach approved; private helper in FileEmitter |
| 1 | PLAN | IMPLEMENT | 3-phase plan approved |

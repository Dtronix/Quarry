# Workflow: 264-fix-qry044-false-positive

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #264
pr:
session: 1
phases-total: 3
phases-complete: 1

## Problem Statement
QRY044 (`InterceptorsNamespacesAnalyzer`) reports false positives when the fully-evaluated `<InterceptorsNamespaces>` MSBuild property seen by the analyzer contains the target `QuarryContext` namespace but is shaped unusually — e.g. contains a leading `;` (empty segment from `$(InterceptorsNamespaces);X` append when upstream was empty), contains three or more entries with the target not last, or contains duplicate entries (notably `Quarry.Generated` appearing twice when `build/Quarry.targets` auto-appends and the consumer also wrote it explicitly).

Reporter (DJGosnell) provided reproductions in issue #264:
- Row 1 fires: `;Logsmith.Generated;MyApp.Data;Quarry.Generated`
- Row 2 fires: `Logsmith.Generated;MyApp.Data;Quarry.Generated`
- Row 3 fires: `Logsmith.Generated;Quarry.Generated;MyApp.Data;Quarry.Generated`
- Row 4 passes: `MyApp.Data;Quarry.Generated`

Reporter's workaround: `<NoWarn>$(NoWarn);QRY044</NoWarn>`.

Suggested direction: ensure membership is checked via `HashSet<string>` after splitting on `;` and filtering empties. Add fixture covering leading `;`, 3+ entries with target not last, and duplicates.

Baseline (session 1 start, commit c113760):
- Quarry.Tests — 2985 passing
- Quarry.Analyzers.Tests — 110 passing
- Quarry.Migration.Tests — 201 passing
- Total — 3296 passing, 0 failures.
- Two NuGet NU1903 warnings on Quarry.Tests (System.Security.Cryptography.Xml 9.0.0). Pre-existing, unrelated to this issue.

## Decisions
- 2026-04-24: Bug does not reproduce against shipped `SplitNamespaces` when editorconfig values are fed directly to the analyzer via `AnalyzerConfigOptionsProvider`. Four unit tests covering all four rows from the issue table pass. Reporter confirmed decompiled IL matches source.
- 2026-04-24: Reporter (DJGosnell) provided a minimal 4-file repro (PackageReferences to Quarry/Quarry.Generator/Quarry.Analyzers 0.3.1 from nuget.org + `<InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>`). Editorconfig evaluates to `" ;MyApp.Data;Quarry.Generated\r"` (leading space + `;`, trailing CR). Only leading `;` distinguishes the failing case from the hardcoded passing case.
- 2026-04-24: Reporter's top hypothesis: `GlobalOptions.TryGetValue("build_property.InterceptorsNamespaces", …)` silently returns `false` for this value shape — editorconfig parser may drop the key when value starts with `;` (treats as comment-ish). Confirmed only by diagnostic probe.
- 2026-04-24: Agreed path: build minimal repro locally wired to worktree-local analyzer DLL, add a temporary diagnostic probe `ReportDiagnostic` in `AnalyzeClassDeclaration` exposing TryGetValue result / raw configured value / HashSet contents, run the build to capture probe output, identify root cause, write real fix. Demo repro deleted after work complete.
- 2026-04-24: **Root cause identified via probe.** Roslyn's editorconfig key-value regex `^\s*([\w\.\-_]+)\s*[=:]\s*(.*?)\s*([#;].*)?$` treats `;` and `#` as inline-comment markers. Non-greedy `(.*?)` captures the value up to the first `;`, and the rest becomes "comment". Probe confirmed: for editorconfig value `" ;MyApp.Data;Quarry.Generated"` the analyzer's `TryGetValue` returns `true` with an empty string; for `"MyApp.Data"` (hardcoded csproj, no `;`) it returns `"MyApp.Data"` (length 10 — NOT `MyApp.Data;Quarry.Generated` that the final MSBuild value contained). This cleanly explains all four rows of the issue's observation table. Bug is in Roslyn; we cannot fix Roslyn.
- 2026-04-24: Fix plan approved — expose alt MSBuild property `QuarryInterceptorsNamespaces = $(InterceptorsNamespaces.Replace(';', '|'))` via `Quarry.Generator.props` AND `Quarry.targets`, have analyzer read the alt property first with the legacy `InterceptorsNamespaces` as fallback. `|` isn't a legal C# namespace character so the replacement is lossless.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-24 | | INTAKE → DESIGN. Loaded issue #264, created worktree, established baseline (3296 tests green). Wrote 4 reproduction unit tests covering all reporter-described editorconfig values — all pass. Asked reporter for diagnostic output; reporter replied with decompiled-IL confirmation + minimal 4-file repro + `;MyApp.Data;Quarry.Generated` editorconfig value. Switching to local-build-based debugging with diagnostic probe. |

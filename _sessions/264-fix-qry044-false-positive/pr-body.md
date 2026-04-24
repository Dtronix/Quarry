## Summary
- Closes #264

## Reason for Change

QRY044 emits false positives whenever the evaluated `<InterceptorsNamespaces>` MSBuild property passed to the analyzer contains more than one entry — which is the normal case, because `build/Quarry.targets` auto-appends `Quarry.Generated`. The reporter observed that only the pathological "one-namespace-list" shape avoids the warning; every realistic multi-namespace project hits it.

Root cause was surfaced via a probe diagnostic compiled into a local analyzer DLL against a minimal repro project: **Roslyn's editorconfig key-value parser treats `;` (and `#`) inside a value as an inline-comment marker.** Its regex `^\s*([\w\.\-_]+)\s*[=:]\s*(.*?)\s*([#;].*)?$` uses a non-greedy value capture, so the instant a `;` appears in the value the parser captures only the text up to that first `;` and drops the rest as "comment". Reading `build_property.InterceptorsNamespaces` therefore under-reads every multi-entry list. Each row of the issue's observation table is explained cleanly by this behavior:

| Raw editorconfig value | Captured by Roslyn | QRY044 fires? |
|---|---|---|
| `;Logsmith.Generated;MyApp.Data;Quarry.Generated` | `""` | yes (empty set) |
| `Logsmith.Generated;MyApp.Data;Quarry.Generated` | `Logsmith.Generated` | yes (target missing) |
| `Logsmith.Generated;Quarry.Generated;MyApp.Data;Quarry.Generated` | `Logsmith.Generated` | yes (target missing) |
| `MyApp.Data;Quarry.Generated` | `MyApp.Data` | no (accidental success — target happens to be first) |

We cannot fix Roslyn's parser. The fix gives the analyzer a value it can read without passing through the buggy `;`-splitting regex.

## Impact

Fix ships in three places:

1. **Analyzer (`Quarry.Analyzers`)** — `InterceptorsNamespacesAnalyzer` now reads `build_property.QuarryInterceptorsNamespaces` (pipe-delimited) first, falling back to the legacy `build_property.InterceptorsNamespaces` (semicolon-delimited, buggy) only when the alt property is absent. Consumers pinned to older Quarry versions keep working on the single-entry happy path.
2. **`Quarry.Generator.props`** — declares `QuarryInterceptorsNamespaces` as a `<CompilerVisibleProperty>` and computes it via a target that runs before `GenerateMSBuildEditorConfigFileCore`. Computation is `$(InterceptorsNamespaces.Replace(';', '|'))` — `|` isn't a legal C# namespace character so the substitution is lossless.
3. **`Quarry/build/Quarry.targets`** — mirrors the same declaration and target so every Quarry consumer gets the alt property regardless of whether `Quarry.Generator` is in their package graph.

Consumer-facing behavior is unchanged apart from the bug fix — same diagnostic ID, same message format, same `.csproj` advice. `<NoWarn>QRY044</NoWarn>` suppressions become dead but harmless.

## Plan items implemented as specified

- **Phase 1**: analyzer preference for pipe-delimited property + `Quarry.Generator.props` declaration + Unit tests covering pipe-path (5 scenarios), legacy-fallback (2 scenarios), precedence-when-both-set (1 scenario). Temporary probe descriptor + `BytesToHex` helper + `Issue264ReproTests.cs` exploratory file all removed before commit.
- **Phase 2**: same declaration + target mirrored into `Quarry.targets` so the Quarry package alone is sufficient for the fix.
- **Phase 3**: no doc changes — public-facing docs describe QRY044 behavior, which is unchanged.

## Deviations from plan implemented

- Initial attempt placed the `.Replace` inside a top-level `<PropertyGroup>`. MSBuild evaluated the property function at import time (before `Quarry.targets` ran to append `Quarry.Generated`), so the exposed value was `|MyApp.Data` only. Fixed by moving the computation into a target with `BeforeTargets="GenerateMSBuildEditorConfigFileCore"` so evaluation is deferred to after all upstream contributions have folded in.
- Initial target used `BeforeTargets="GenerateMSBuildEditorConfigFile"`. The actual SDK target that does the work is `GenerateMSBuildEditorConfigFileCore` (the former is a dependency-umbrella that runs after its own dependencies). Verified via `dotnet build -v:diag` target trace.

## Gaps in original plan implemented

Review surfaced three small hardenings, applied in the remediation commit:

- **B#8**: documented the analyzer's build-time reliance on `GenerateMSBuildEditorConfigFileCore` being a stable Roslyn SDK target name. If a future .NET SDK renames it, QRY044 silently falls back to the legacy (buggy) path.
- **B#13**: added `EmptyPipeValueFallsBackToLegacyProperty` — covers the branch where the pipe property is declared but empty-string (e.g. older Quarry exposed the property but MSBuild evaluated it to empty).
- **B#22**: moved the long explanatory XML comment from inside `<ItemGroup>` (adjacent to but not describing any item) to above the `<Target>` it actually documents.

## Migration Steps

None. Consumers should `dotnet restore` after upgrading the Quarry package (normal NuGet upgrade workflow). No csproj changes required. Existing `<InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>` csproj fragments continue to work. Consumers who added `<NoWarn>$(NoWarn);QRY044</NoWarn>` as a workaround can safely remove it.

## Performance Considerations

Negligible. One additional MSBuild target firing per build before `GenerateMSBuildEditorConfigFileCore`; target body is a single property-function call. One additional key in the generated editorconfig file. Analyzer code path adds one extra `TryGetValue` + `IsNullOrWhiteSpace` check in the pipe-preferring branch before identical-shape HashSet construction.

## Security Considerations

No new surface. The alt property is a build-time string exposed via `<CompilerVisibleProperty>` and read only by the analyzer; no user input flows through any code-generation path. `|` is an inert separator character.

## Breaking Changes

### Consumer-facing
None.

### Internal
- `InterceptorsNamespacesAnalyzer.ReadOptedNamespaces` is new; replaces the inline `SplitNamespaces` call that previously read only `build_property.InterceptorsNamespaces`. `SplitNamespaces` gains a `separator` parameter (was hardcoded to `;`).
- `Quarry.Generator.props` and `Quarry.targets` each gain a single internal MSBuild target (`_QuarryComputeInterceptorsNamespaces` and `_QuarryComputeInterceptorsNamespacesFromTargets` respectively) and a new `<CompilerVisibleProperty Include="QuarryInterceptorsNamespaces">`. Target names begin with `_` per the MSBuild convention for internal non-extension-point targets.

## Test Coverage

- `Quarry.Analyzers.Tests`: 117 passing (was 110 at baseline; +6 pipe-path tests, +2 legacy-fallback tests, +1 precedence test, -2 existing tests refactored into the new shape with `SingleContextSource` shared fixture, +1 empty-pipe-fallback remediation test).
- Full solution: 3303 passing (3296 baseline + 7 net new).

## Verification

Reproduced the bug against nuget.org-shipped `Quarry.Analyzers@0.3.1` via a minimal 4-file project matching the reporter's description. Verified the local-built fix against the same project by substituting in the local-built `Quarry.Analyzers.dll` and the local-worktree `Quarry.Generator.props` and `Quarry.targets`. All four observation-table shapes (leading `;`, target-not-last, duplicate entries, hardcoded single entry) correctly produce no QRY044 under the fix. Diagnostic probe added and removed within this branch — not visible in the final diff.

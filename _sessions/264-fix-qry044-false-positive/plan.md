# Plan: Fix QRY044 false-positive (#264)

## Root cause

Roslyn's editorconfig parser uses a non-greedy regex against key-value lines:
```
^\s*([\w\.\-_]+)\s*[=:]\s*(.*?)\s*([#;].*)?$
```

The trailing optional group `([#;].*)?` treats `;` (or `#`) anywhere in the value as an inline-comment marker. Because `.*?` is non-greedy, the parser captures only the substring up to the first `;`, discarding everything from the first `;` onward.

For `build_property.InterceptorsNamespaces = ;MyApp.Data;Quarry.Generated`:
- regex captures value = `""` (empty)
- the rest is treated as a comment

For `build_property.InterceptorsNamespaces = MyApp.Data;Quarry.Generated`:
- regex captures value = `MyApp.Data`
- `;Quarry.Generated` dropped as comment

The `SplitNamespaces`/`HashSet<string>` logic in the analyzer is correct — it just never receives the full value because Roslyn truncates it at the first `;`. Every row of the issue's observation table is consistent with this behavior.

We cannot fix Roslyn's parser. We must provide the analyzer a value it can read without semicolons.

## Solution

Expose the interceptor-namespaces list to the analyzer through a **second MSBuild property** whose value uses `|` as the delimiter, so no `;` ever appears in the editorconfig value. The analyzer reads the new property first and falls back to the legacy property only when the new one is absent (older `Quarry.Generator` package, or consumer who manually set `InterceptorsNamespaces` without picking up the shipped `.props`).

Key concept — **pipe-delimited normalized list**. `InterceptorsNamespaces` (the MSBuild property the compiler consumes) is semicolon-delimited. We leave that property untouched so csc's `/features:` flag continues to receive the canonical value. We introduce a sibling property `QuarryInterceptorsNamespaces` whose value is `$(InterceptorsNamespaces)` with `;` replaced by `|`. That sibling is the one exposed to analyzers via `<CompilerVisibleProperty>`. `|` is not in Roslyn's comment-marker set and is not a legal C# namespace character, so the `.Replace(';', '|')` round-trip is lossless for well-formed values.

MSBuild property function that does the translation, written inside a `<PropertyGroup>` in `Quarry.Generator.props`:
```xml
<QuarryInterceptorsNamespaces>$(InterceptorsNamespaces.Replace(';', '|'))</QuarryInterceptorsNamespaces>
```
MSBuild evaluates property function expansions lazily at consumption time, so `QuarryInterceptorsNamespaces` is a string-substitution view of `InterceptorsNamespaces` as it stands when the editorconfig file is generated — by which point all upstream .props, csproj, and .targets contributions have flowed into `InterceptorsNamespaces`.

On the analyzer side, `SplitNamespaces` gains a second overload that splits on `|` when a pipe is present, falling back to the current `;` split otherwise. The `AnalyzeClassDeclaration` method reads `QuarryInterceptorsNamespaces` via `TryGetValue` first; if absent, it falls back to `InterceptorsNamespaces`. The same trim/filter-empty/HashSet logic applies to both code paths.

## Phases

### Phase 1 — Fix the analyzer and the props (code + unit tests)

1. `src/Quarry.Generator/build/Quarry.Generator.props`:
   - Add a `<PropertyGroup>` that sets `QuarryInterceptorsNamespaces` to `$(InterceptorsNamespaces.Replace(';', '|'))`.
   - Add `<CompilerVisibleProperty Include="QuarryInterceptorsNamespaces" />` to the existing `ItemGroup`.
   - Keep the existing `<CompilerVisibleProperty Include="InterceptorsNamespaces" />` as a fallback (old-props consumers).

2. `src/Quarry.Analyzers/InterceptorsNamespacesAnalyzer.cs`:
   - In `AnalyzeClassDeclaration`, read `build_property.QuarryInterceptorsNamespaces` first. If present and non-empty after trimming, split on `|`. Else read `build_property.InterceptorsNamespaces` and split on `;` (legacy path).
   - Consolidate the split/trim/filter into a single helper accepting the split character.
   - Remove the temporary `QRY999` probe descriptor and all probe-emission code added during debugging. Restore `SupportedDiagnostics` to `InterceptorsNamespaceMissing` alone.
   - Remove the `BytesToHex` helper.

3. `src/Quarry.Analyzers/AnalyzerDiagnosticDescriptors.cs`:
   - Remove the `InterceptorsProbe` descriptor (QRY999).

4. `src/Quarry.Analyzers.Tests/`:
   - Delete `Issue264ReproTests.cs` (the exploratory file — its premise was wrong, the real regression coverage goes into the main test file).
   - `InterceptorsNamespacesAnalyzerTests.cs`:
     - Add a second arg to the test harness so tests can set the new `build_property.QuarryInterceptorsNamespaces` value in addition to the legacy one.
     - Add tests for the new pipe-delimited reading path:
       - `QuarryInterceptorsNamespaces = "|MyApp.Data|Quarry.Generated"` (leading `|`, because upstream `$(InterceptorsNamespaces)` was empty and `;` got replaced) — must NOT fire.
       - `QuarryInterceptorsNamespaces = "MyApp.Data|Quarry.Generated"` — must NOT fire.
       - `QuarryInterceptorsNamespaces = "Logsmith.Generated|MyApp.Data|Quarry.Generated"` — must NOT fire (target-not-last, the row-2 scenario that was actually breaking).
       - `QuarryInterceptorsNamespaces = "Logsmith.Generated|Quarry.Generated|MyApp.Data|Quarry.Generated"` — must NOT fire (duplicate entries).
       - `QuarryInterceptorsNamespaces = "Quarry.Generated"` (target absent) — MUST fire.
     - Add tests for legacy-fallback path (new property absent, old property set):
       - `InterceptorsNamespaces = "MyApp.Data;Quarry.Generated"` — must NOT fire.
       - `InterceptorsNamespaces = "Quarry.Generated"` — MUST fire.
     - Add a test where both properties are set and they disagree — the new property wins (ensures fallback only triggers when new is absent).

### Phase 2 — Ship a targets-side workaround for old-props / manual consumers

Consumers who installed `Quarry` and `Quarry.Analyzers` but NOT `Quarry.Generator` don't get `Quarry.Generator.props` and therefore don't get the new `QuarryInterceptorsNamespaces` property. They'd fall back to the buggy legacy path and — until they update — still hit the false-positive. Fix this by also shipping the alt-property declaration from the already-consumed-by-everyone package: `Quarry`'s `build/Quarry.targets`.

1. `src/Quarry/build/Quarry.targets`:
   - After the existing `<InterceptorsNamespaces>$(InterceptorsNamespaces);Quarry.Generated</InterceptorsNamespaces>` line, add a `<PropertyGroup>` that also sets `QuarryInterceptorsNamespaces` to `$(InterceptorsNamespaces.Replace(';', '|'))`.
   - Targets can't declare `<CompilerVisibleProperty>` directly (or rather, they can but it's non-idiomatic — `.props` is the normal place), but an `<ItemGroup>` with `<CompilerVisibleProperty Include="QuarryInterceptorsNamespaces" />` in the targets file works the same way.

That way, even if `Quarry.Generator.props` isn't imported, every Quarry consumer who has the `Quarry` package (which is mandatory) picks up the alt property and the analyzer sees a usable value.

Dependency note: Phase 2 can technically be done before Phase 1 but reviewing them separately is easier with Phase 1 committed first — the targets-side change is just redundant coverage of Phase 1's mechanism.

### Phase 3 — Documentation touch-ups

1. `src/Quarry.Generator/README.md`, `src/Quarry.Generator/llm.md`, `llm.md`:
   - Update the QRY044 entry if it calls out `build_property.InterceptorsNamespaces` by name (analyzer now prefers the alt property, falls back to the legacy one).
   - Otherwise no doc change — the user-facing diagnostic message and the csproj advice are unchanged.

2. No release-note addition — release notes are maintained per `docs/articles/releases/` in a separate workflow.

## Tests to add or modify

Covered in Phase 1 and Phase 2 sections. Summary:

- Unit tests covering the new `|`-delimited reading path (5 scenarios).
- Unit tests covering legacy fallback (2 scenarios).
- Unit test verifying new wins over legacy when both are present.
- No integration test against a real MSBuild build — the unit tests exercise the analyzer's `AnalyzerConfigOptionsProvider` contract, and we can't easily simulate Roslyn's editorconfig regex-parsing bug in unit tests anyway (it's a parser we don't control). The closest thing would be a post-build verification on `src/Quarry.Analyzers.Tests` using a csproj similar to the minimal repro — skip, out of scope for this fix.

Existing tests that stay untouched: `NoDiagnostic_WhenContextNamespaceIsOptedIn`, `EmitsQRY044_WhenContextNamespaceMissing`, `NoDiagnostic_ForContextInGlobalNamespace`, `FlagsOnlyMissingNamespaces_WhenMultipleContexts`, `EmitsQRY044_WhenInterceptorsNamespacesPropertyIsAbsent`, `EmitsQRY044_WhenAttributeUsesAliasQualifiedName`, `NoDiagnostic_ForNonContextClass`. These exercise the legacy-only path since they only set one property; after the code change they'll go through the fallback branch and should continue to pass unchanged.

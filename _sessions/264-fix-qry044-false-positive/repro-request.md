# Diagnostic request — Issue #264 (QRY044 false-positive) cannot reproduce from analyzer source

We're the maintainers looking at #264. When we exercise the shipped `InterceptorsNamespacesAnalyzer` code path directly with the exact four editorconfig values from the issue table, **all four scenarios pass (no QRY044 fires)** — including row 2 (`Logsmith.Generated;MyApp.Data;Quarry.Generated`) which is the "broken" shape per the report. So either the shipped NuGet isn't running the code we think it is, or something in your build pipeline is feeding the analyzer a different value than the one your editorconfig shows.

Before we merge a "no-op hardening" fix, we'd like you to run the diagnostic steps below to pin down which one it is.

## What the shipped analyzer actually does (v0.3.1, commit c113760)

`src/Quarry.Analyzers/InterceptorsNamespacesAnalyzer.cs`:

```csharp
var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
if (!options.TryGetValue("build_property.InterceptorsNamespaces", out var configured))
    configured = string.Empty;

var opted = SplitNamespaces(configured);
if (opted.Contains(namespaceName))
    return;
// ... report QRY044

private static HashSet<string> SplitNamespaces(string value)
{
    var set = new HashSet<string>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(value))
        return set;
    foreach (var part in value.Split(';'))
    {
        var trimmed = part.Trim();
        if (trimmed.Length > 0)
            set.Add(trimmed);
    }
    return set;
}
```

`namespaceName` is `symbol.ContainingNamespace.ToDisplayString()`.

This is the exact shape the issue recommends. It handles leading `;`, trailing `;`, duplicates, and the target in any position.

## What we verified in-repo

Four unit tests (one per row of your issue table), each feeding the exact editorconfig value directly to `InterceptorsNamespacesAnalyzer` via `AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.InterceptorsNamespaces", …)`:

| Row | editorconfig value under test | Expected | Actual |
|---|---|---|---|
| 1 | `;Logsmith.Generated;MyApp.Data;Quarry.Generated` | no QRY044 | **no QRY044** ✓ |
| 2 | `Logsmith.Generated;MyApp.Data;Quarry.Generated` | no QRY044 | **no QRY044** ✓ |
| 3 | `Logsmith.Generated;Quarry.Generated;MyApp.Data;Quarry.Generated` | no QRY044 | **no QRY044** ✓ |
| 4 | `MyApp.Data;Quarry.Generated` | no QRY044 | **no QRY044** ✓ |

All four pass. The analyzer's actual code path does not produce the reported false positive when fed these inputs.

## Hypotheses we can't rule out from our side

1. **Analyzer DLL mismatch.** The analyzer loaded into your IDE/compiler process isn't `Quarry.Analyzers 0.3.1` — it's an older cached copy, or the wrong NuGet package graph pulled a different build.
2. **`build_property.InterceptorsNamespaces` not actually exposed.** `CompilerVisibleProperty Include="InterceptorsNamespaces"` ships in `Quarry.Generator/build/Quarry.Generator.props` — NOT in `Quarry.Analyzers`. If `Quarry.Generator.props` didn't import for you for any reason, the analyzer sees `TryGetValue → false` and treats `configured = string.Empty`. Then `{}.Contains("MyApp.Data") → false`, and QRY044 fires regardless of what's in the editorconfig. The editorconfig value is irrelevant if the property isn't compiler-visible to the analyzer.
3. **Wrong `namespaceName` being checked.** `symbol.ContainingNamespace.ToDisplayString()` returns something other than `"MyApp.Data"` in your real build — e.g. file-scoped vs. block-scoped namespace differences, global alias qualifier, or a trailing invisible character.
4. **QRY044 from somewhere else.** Some other tool in your pipeline (custom analyzer, ruleset, suppression rewrite) is emitting id `"QRY044"`.

Hypothesis 2 is the strongest: the editorconfig value you're grepping and the value the analyzer actually sees via `build_property.*` are two different things. The `<CompilerVisibleProperty>` declaration is what bridges them, and it ships in a *different NuGet package* than the analyzer.

## What to report back

Please run these and paste the raw output. Don't paraphrase.

### 1. Which analyzer DLL is actually loaded

```
dotnet build -v:detailed 2>&1 | grep -i "Analyzer\|Quarry\.Analyzers" | head -50
```

Specifically: find the full path of the loaded `Quarry.Analyzers.dll` and its version stamp. If there are multiple paths, that's a clue.

```
powershell -c "(Get-Item '<path-to-Quarry.Analyzers.dll>').VersionInfo | Format-List *"
```

### 2. Verify `build_property.InterceptorsNamespaces` is in the editorconfig the analyzer sees

From the obj/ of the failing project:

```
grep -rn "build_property\.InterceptorsNamespaces\|is_global" obj/**/*.editorconfig 2>/dev/null
```

Expected: exactly one line `build_property.InterceptorsNamespaces = <value>`, preceded somewhere by `is_global = true`. If `is_global = true` is missing, the file is per-path instead of global and the analyzer won't read it.

Also show **the first 20 lines** of each `*.GeneratedMSBuildEditorConfig.editorconfig` to confirm the `is_global` header:

```
ls obj/**/*.GeneratedMSBuildEditorConfig.editorconfig
head -20 obj/<tfm>/<config>/<project>.GeneratedMSBuildEditorConfig.editorconfig
```

### 3. Confirm `Quarry.Generator.props` actually imported

```
dotnet msbuild /pp:/tmp/preprocessed.xml <yourproj>.csproj
grep -i "Quarry.Generator.props\|CompilerVisibleProperty" /tmp/preprocessed.xml | head -20
```

You need `CompilerVisibleProperty Include="InterceptorsNamespaces"` to appear in the preprocessed MSBuild output. If it doesn't, the analyzer can't see the value at all — and QRY044 will fire regardless of what the csproj contains, because `TryGetValue` returns false.

### 4. What the analyzer actually receives — attach a binlog and inspect

```
dotnet build -bl
```

Then open `msbuild.binlog` in MSBuild Structured Log Viewer → find target `CoreCompile` → show the `<AnalyzerConfigFiles>` item group passed to csc. Paste the list of paths, and the first 30 lines of each one that isn't in `%USERPROFILE%\.nuget\packages`.

### 5. The minimal reproduction

Zip and share the smallest csproj + one `.cs` file that reproduces row 2's false positive against Quarry 0.3.1 + Quarry.Generator 0.3.1 + Quarry.Analyzers 0.3.1 from nuget.org (not a local build). We'll drop that into a clean worktree and either reproduce or identify the environmental factor.

## What we're going to do regardless

Add the three-scenario regression fixture you suggested (leading `;`, target-not-last in 3+ list, duplicate entries) as unit tests, so if this logic ever regresses we catch it. That's a cheap insurance policy whether or not we can reproduce the bug you're seeing.

We'd rather ship a real fix than a no-op hardening, though, so the diagnostic output above is what would let us do that.

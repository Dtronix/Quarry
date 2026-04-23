# Plan: 259-docs-dx-friction-points

Five independently committable phases. Phases are ordered so earlier work doesn't depend on later work. Each phase ends with a green test run before committing.

---

## Phase 1 — QRY043: reject un-materializable row-entity shapes

**Why first.** Smallest self-contained change. Pure diagnostic addition — no interceptor-codegen rewiring. Unblocks a user hitting the `CS7036` / `CS8852` compile errors by surfacing the real reason.

**What counts as un-materializable.** The generator's row materializer (`new T()` + property assignment) needs:
- A publicly-accessible parameterless constructor. Positional records (`record MyRow(Guid Id, string Name)`) don't have one — their auto-generated constructor takes the positional parameters, and there's no default.
- Writeable (non-`init`) public properties. `init`-only properties can only be assigned inside an object initializer. The current property filter at `UsageSiteDiscovery.cs:4040` admits them because `SetMethod != null` is true for init accessors (Roslyn exposes the init accessor as `SetMethod` with `IsInitOnly == true`).

**Detection point.** `DisplayClassEnricher.EnrichRawSqlTypeInfo` runs after the supplemental compilation is built and has the full `ITypeSymbol`. Add validation there, alongside the existing `ResolveRawSqlTypeInfo` call. Only run the validation when `TypeKind != Scalar` (scalars are built-ins).

**Carrying the diagnostic out.** Add a nullable `string? MaterializabilityError` field to `RawCallSite` (and surface it through the `Bound.Raw` indirection that `TranslatedCallSite` already uses). `PipelineOrchestrator.CollectTranslatedDiagnostics` reports it to the diagnostics list, just like QRY031.

**Implementation sketch.**

```csharp
// In DisplayClassEnricher, after ResolveRawSqlTypeInfo succeeds for a Dto:
if (rawSqlTypeInfo.TypeKind != RawSqlTypeKind.Scalar)
{
    if (typeArgSymbol is INamedTypeSymbol named)
    {
        var hasParameterless = named.Constructors
            .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
        var hasInitOnly = named.GetMembers().OfType<IPropertySymbol>()
            .Any(p => p.DeclaredAccessibility == Accessibility.Public
                      && p.SetMethod is { IsInitOnly: true });

        if (!hasParameterless)
            materializabilityError = "no accessible parameterless constructor";
        else if (hasInitOnly)
            materializabilityError = "one or more properties are init-only";
    }
}
```

**Diagnostic descriptor.**

```csharp
// In Quarry.Generator/DiagnosticDescriptors.cs, after QRY041:
public static readonly DiagnosticDescriptor RowEntityNotMaterializable = new(
    id: "QRY043",
    title: "Row entity type is not materializable",
    messageFormat: "Row entity type '{0}' cannot be materialized by the source generator: {1}. "
                 + "Row types passed to RawSqlAsync<T> must be classes or structs with a public "
                 + "parameterless constructor and public get/set properties. Positional records "
                 + "and init-only properties are not supported — project into an immutable shape "
                 + "via Select(x => new MyDto { ... }) instead.",
    category: Category,
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Tests.**
- `src/Quarry.Tests/Generation/RawSqlInterceptorTests.cs`: add tests that invoke the generator on (a) a positional record row type and (b) a class with init-only properties. Assert QRY043 is reported in both cases. Use the existing `QueryTestHarness` / generator test infrastructure.
- Add a positive test confirming no QRY043 is raised for a valid plain class row type.

**Commit message.** `feat(generator): QRY043 diagnostic for un-materializable RawSqlAsync row types`

---

## Phase 2 — Support nested row-entity types in interceptor emission

**Why this order.** Independent from Phase 1. Touches generator codegen rather than diagnostics. Done before the docs phase so the doc wording reflects the new supported surface.

**Current bug.** `FileEmitter.Emit()` line 90-94 builds `entityNamespaces` by string-parsing the last `.`-separated segment off each `EntityTypeName`. For a nested type `MyApp.Data.Outer.Row`, this yields `MyApp.Data.Outer`, which is then emitted as `using MyApp.Data.Outer;` — but `Outer` is a type, not a namespace, so the generated file doesn't compile.

**Fix strategy.** Resolve the actual namespace from the Roslyn symbol at discovery time and store it on the call site. Store a fully-qualified result type name alongside the short one so interceptor bodies can reference nested types without needing a `using`.

**Model changes.**

```csharp
// RawSqlTypeInfo adds two fields:
public bool IsNestedType { get; }
public string FullyQualifiedResultTypeName { get; }

// RawCallSite adds one field:
public string? EntityNamespace { get; }  // namespace string, e.g. "MyApp.Data" — or null for global
```

`TranslatedCallSite` already surfaces `Bound.Raw`, so consumers read through that.

**Discovery changes.**

In `UsageSiteDiscovery.ResolveRawSqlTypeInfo`:

```csharp
var isNestedType = typeSymbol.ContainingType != null;
var fullyQualifiedName = typeSymbol.ToDisplayString(
    SymbolDisplayFormat.FullyQualifiedFormat);  // e.g. "global::MyApp.Data.Outer.Row"
```

In `DiscoverRawSqlUsageSite` (and everywhere else that builds a `RawCallSite`):

```csharp
var entityNamespace = typeArgSymbol.ContainingNamespace?.IsGlobalNamespace == true
    ? null
    : typeArgSymbol.ContainingNamespace?.ToDisplayString();
```

**Emitter changes.**

`FileEmitter.Emit()` replaces the string-parse call:

```csharp
// OLD:
var entityNamespaces = _sites
    .Select(s => InterceptorCodeGenerator.GetNamespaceFromTypeName(s.EntityTypeName))
    .Where(ns => !string.IsNullOrEmpty(ns) && ns != "Quarry" && ns != "System")
    .Distinct().ToList();

// NEW:
var entityNamespaces = _sites
    .Select(s => s.EntityNamespace)
    .Where(ns => !string.IsNullOrEmpty(ns) && ns != "Quarry" && ns != "System")
    .Distinct().ToList();
```

`RawSqlBodyEmitter` — where `resultType = rawSqlInfo.ResultTypeName` is written into the generated C#, use the FQN for nested types:

```csharp
var resultType = rawSqlInfo.IsNestedType
    ? rawSqlInfo.FullyQualifiedResultTypeName
    : rawSqlInfo.ResultTypeName;
```

The struct-name generation `$"RawSqlReader_{site.RawSqlTypeInfo.ResultTypeName}_{rawSqlStructIndex}"` at `FileEmitter.cs:269` keeps `ResultTypeName` (short form) because struct identifiers can't contain `::` or `.`. For nested types, `ResultTypeName` would be something like `Row` which is a valid identifier.

**Leave `GetNamespaceFromTypeName` alone.** It's still used for context-namespace collection via `s.ContextNamespace` and chain-schema namespaces which carry their own symbol-resolved namespace. Only the RawSql entity-namespace path needs the fix.

**Tests.**
- `RawSqlInterceptorTests.cs`: add a test where the row type is a nested `public sealed class Outer { public sealed class Row { get; set; } }` pattern. Assert the generator compiles cleanly (no `CS0138`) and the interceptor references the row type via FQN.
- Add a test for a namespace-level row type confirming emission is unchanged (no regression — still uses short name + `using`).

**Commit message.** `feat(generator): support nested row-entity types in RawSqlAsync interceptors`

---

## Phase 3 — Ship `build/Quarry.targets` auto-adding `Quarry.Generated`

**Why third.** Doesn't depend on Phase 1 or 2. Placed before the analyzer phase so the analyzer can be written knowing `Quarry.Generated` is already handled.

**New file.** `src/Quarry/build/Quarry.targets`:

```xml
<Project>
  <!--
    Quarry emits interceptors into the Quarry.Generated namespace for generic helpers.
    Auto-register it so consumers don't need to add it manually. Consumers still
    add the namespace of their own QuarryContext subclass(es). See QRY044.
  -->
  <PropertyGroup>
    <InterceptorsNamespaces>$(InterceptorsNamespaces);Quarry.Generated</InterceptorsNamespaces>
  </PropertyGroup>
</Project>
```

**Packaging.** Add to `src/Quarry/Quarry.csproj`:

```xml
<ItemGroup>
  <None Include="build\**" Pack="true" PackagePath="build\" />
</ItemGroup>
```

NuGet looks for `build/{PackageId}.targets` and auto-imports it into consumer projects. Quarry.Generator already ships `build\Quarry.Generator.props` the same way (see `Quarry.Generator.csproj:36-38`).

**Also expose InterceptorsNamespaces as CompilerVisibleProperty.** Update `src/Quarry.Generator/build/Quarry.Generator.props`:

```xml
<Project>
  <ItemGroup>
    <CompilerVisibleProperty Include="QuarrySqlManifestPath" />
    <CompilerVisibleProperty Include="ProjectDir" />
    <CompilerVisibleProperty Include="InterceptorsNamespaces" />
  </ItemGroup>
</Project>
```

This lets the QRY044 analyzer (Phase 4) read the property from `AnalyzerConfigOptions`.

**Cleanup.** Audit in-repo sample/test projects that currently list `Quarry.Generated` manually. The targets file will now add it automatically — manual entries become redundant but harmless (`;` separator de-duplicates semantically). Leave manual entries alone to avoid a separate concern in this PR; follow-up cleanup can be tracked separately if wanted.

**Tests.** Packaging is difficult to unit-test. Verification is manual:
- Build the Quarry package (`dotnet pack src/Quarry`).
- Inspect `bin/Debug/Quarry.*.nupkg` with a zip tool; confirm `build/Quarry.targets` is present.
- In a temporary consumer project, reference the local Quarry package; confirm `InterceptorsNamespaces` contains `Quarry.Generated` without a manual `<InterceptorsNamespaces>` entry.

Run the full test suite to ensure no regressions. No new automated tests — this phase is pure packaging.

**Commit message.** `feat(packaging): ship Quarry.targets auto-opting into the Quarry.Generated interceptors namespace`

---

## Phase 4 — QRY044 analyzer: detect `[QuarryContext]` not opted into `InterceptorsNamespaces`

**Why this order.** Depends on Phase 3 exposing `InterceptorsNamespaces` as a `CompilerVisibleProperty`.

**Analyzer.** New `src/Quarry.Analyzers/Rules/InterceptorsNamespacesAnalyzer.cs`:

- `DiagnosticAnalyzer` with `SymbolKind.NamedType` action.
- For each class with the `[QuarryContext]` attribute:
  - Read `AnalyzerConfigOptions.GlobalOptions["build_property.InterceptorsNamespaces"]`.
  - Parse into `;`-separated set.
  - Compare against the containing namespace of the class.
  - If absent, raise QRY044 on the class identifier location.

**Diagnostic descriptor.** In `src/Quarry.Analyzers/AnalyzerDiagnosticDescriptors.cs`, alongside the existing QRY042:

```csharp
public static readonly DiagnosticDescriptor InterceptorsNamespaceMissing = new(
    id: "QRY044",
    title: "QuarryContext namespace not opted into InterceptorsNamespaces",
    messageFormat: "QuarryContext subclass '{0}' is declared in namespace '{1}' but that namespace "
                 + "is not listed in <InterceptorsNamespaces>. The C# 12 interceptors feature requires "
                 + "every emitting namespace to be opted in. Add this to your .csproj: "
                 + "<InterceptorsNamespaces>$(InterceptorsNamespaces);{1}</InterceptorsNamespaces>",
    category: Category,
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true,
    description: "Without the opt-in, the compiler reports CS9137 at build time. This diagnostic "
               + "surfaces the problem earlier with the exact project-file edit.");
```

**Severity = Warning** (not Error) because:
- The build will already fail with `CS9137` anyway if the opt-in is missing.
- QRY044 is an *early* surfacing of the problem, not a new build-breaker.
- If the user intentionally sets `InterceptorsNamespaces` via a `Directory.Build.props` the analyzer can't see, demoting to warning avoids false positives.

**Skip registration.** `QuarryQueryAnalyzer.cs` is the existing aggregator — register the new analyzer alongside it so consumers get the rule automatically.

**Tests.** `src/Quarry.Analyzers.Tests/`:
- Test cases using the existing Roslyn test SDK (`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`).
- Case 1: `[QuarryContext]` class in namespace `MyApp.Data`, `InterceptorsNamespaces=Quarry.Generated;MyApp.Data` → no diagnostic.
- Case 2: `[QuarryContext]` class in namespace `MyApp.Data`, `InterceptorsNamespaces=Quarry.Generated` → QRY044 emitted, message contains the exact line.
- Case 3: `[QuarryContext]` class in global namespace → no diagnostic (empty namespace, special-case: `Quarry.Generated` fallback already used by the generator).
- Case 4: multiple `[QuarryContext]` classes across different namespaces, mixed opt-in status → QRY044 on the missing one only.

**Commit message.** `feat(analyzers): QRY044 warns when QuarryContext namespace is missing from InterceptorsNamespaces`

---

## Phase 5 — Documentation updates (`llm.md`)

**Why last.** Lets docs reflect final committed behavior. Also cheap to revise if Phase 1-4 reveal surprises.

**Changes to `llm.md`.**

1. **New section "Project Setup"** (inserted before `### Context`, around line 72, as a subsection of "Usage"):

    ```markdown
    ### Project Setup

    Quarry uses C# 12 interceptors, which require every emitting namespace to be
    opted in via MSBuild. Quarry's NuGet package auto-registers `Quarry.Generated`
    (used for generic helpers) via `build/Quarry.targets`. You must also register
    the namespace of each `QuarryContext` subclass:

    ```xml
    <PropertyGroup>
      <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
    </PropertyGroup>
    ```

    If you forget, the analyzer emits QRY044 with the exact line to paste.
    ```

2. **Updated `InterceptorsNamespaces` paragraph** (currently lines 99-102). Adjust to say the context's namespace only (Quarry.Generated is auto). Cross-reference QRY044.

3. **New "Row entity requirements" note under Raw SQL** (near line 226):

    ```markdown
    **Row types** passed to `RawSqlAsync<T>` must have:
    - a public parameterless constructor
    - public `get; set;` properties (not `init`-only)

    Positional records and init-only properties are not compatible with the
    source-generated materializer; QRY043 reports them at compile time. For
    immutable result shapes, use `Select(x => new Dto { ... })` on a chain query.
    ```

4. **Add QRY043 and QRY044 to the Diagnostics table** (if there is one in `llm.md`) and in `src/Quarry.Generator/README.md` / `src/Quarry.Generator/llm.md` which also track diagnostic inventory.

**Tests.** None — docs-only phase. Verify the full test suite still passes after this is the final commit.

**Commit message.** `docs: document row-entity shape requirements and InterceptorsNamespaces auto-opt-in`

---

## Dependency graph

```
Phase 1 (QRY043)           ─┐
Phase 2 (nested types)     ─┤──── independent, any order between them
Phase 3 (.targets)         ─┤
Phase 4 (QRY044 analyzer)  ─── depends on Phase 3 (CompilerVisibleProperty)
Phase 5 (docs)             ─── depends on all (docs describe final behavior)
```

## Risks

- **Nested-type support (Phase 2)** might touch the non-RawSql generator path if `EntityNamespace` is used more broadly than planned. Keep the change strictly scoped to `FileEmitter.Emit()`'s `entityNamespaces` collection.
- **Incremental-generator equality** (Phase 1, 2): `RawSqlTypeInfo` gains new fields — `Equals` / `GetHashCode` must be updated or incremental caching produces stale results. Same concern for `RawCallSite`.
- **Analyzer config read timing** (Phase 4): `AnalyzerConfigOptions` may not surface `build_property.InterceptorsNamespaces` until after the targets file (Phase 3) is packaged and consumed. Tests must set the analyzer-config value explicitly, not rely on a live build.
- **Sample project redundant entries** (Phase 3): in-repo test projects list `Quarry.Generated` manually. The targets file will also add it; semicolon-separated lists deduplicate semantically, but leave entries as-is for this PR.

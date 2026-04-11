# Plan: Add shared interfaces for migration converter result types

## Key Concepts

**IConversionDiagnostic** — A shared interface for all converter diagnostic types. Every diagnostic across all 4 converters has identical shape: `Severity` (string) and `Message` (string).

**IConversionEntry** — A shared interface for all converter entry types. Exposes the 7 common properties that all entry types share: `FilePath`, `Line`, `ChainCode`, `IsConvertible`, `HasWarnings`, `OriginalSource` (new unified name), and `Diagnostics` (typed as `IReadOnlyList<IConversionDiagnostic>`).

**Explicit interface implementation for Diagnostics** — Each concrete entry type has a strongly-typed `Diagnostics` property (e.g., `IReadOnlyList<DapperConversionDiagnostic>`). The interface requires `IReadOnlyList<IConversionDiagnostic>`. Since `IReadOnlyList<T>` is covariant, we use explicit interface implementation: `IReadOnlyList<IConversionDiagnostic> IConversionEntry.Diagnostics => Diagnostics;`. This preserves the existing strong typing for consumers while enabling unified processing.

**OriginalSource** — New property on the interface. Maps to `OriginalSql` on Dapper/AdoNet entry types and `OriginalCode` on EfCore/SqlKata entry types. Implemented via explicit interface implementation so existing public APIs are unchanged.

## Phases

### Phase 1: Add IConversionDiagnostic interface and implement on all diagnostic types

Create `IConversionDiagnostic.cs` in `src/Quarry.Migration/` with two properties:
```csharp
public interface IConversionDiagnostic
{
    string Severity { get; }
    string Message { get; }
}
```

Modify the 4 diagnostic classes to implement it:
- `DapperConversionDiagnostic : IConversionDiagnostic`
- `EfCoreConversionDiagnostic : IConversionDiagnostic`
- `AdoNetConversionDiagnostic : IConversionDiagnostic`
- `SqlKataConversionDiagnostic : IConversionDiagnostic`

No explicit implementation needed — properties already match exactly.

**Tests:** Add tests in a new `InterfaceTests.cs` that verify each diagnostic type is assignable to `IConversionDiagnostic` and that properties are accessible through the interface.

### Phase 2: Add IConversionEntry interface and implement on all entry types

Create `IConversionEntry.cs` in `src/Quarry.Migration/` with 7 properties:
```csharp
public interface IConversionEntry
{
    string FilePath { get; }
    int Line { get; }
    string? ChainCode { get; }
    IReadOnlyList<IConversionDiagnostic> Diagnostics { get; }
    bool IsConvertible { get; }
    bool HasWarnings { get; }
    string OriginalSource { get; }
}
```

Modify the 4 entry classes to implement it. Each needs explicit implementation for two properties:

1. **Diagnostics** — Explicit implementation returns the existing strongly-typed list (covariance handles the type conversion):
   ```csharp
   IReadOnlyList<IConversionDiagnostic> IConversionEntry.Diagnostics => Diagnostics;
   ```

2. **OriginalSource** — Explicit implementation returns the existing converter-specific property:
   - Dapper: `string IConversionEntry.OriginalSource => OriginalSql;`
   - EfCore: `string IConversionEntry.OriginalSource => OriginalCode;`
   - AdoNet: `string IConversionEntry.OriginalSource => OriginalSql;`
   - SqlKata: `string IConversionEntry.OriginalSource => OriginalCode;`

The remaining 4 properties (`FilePath`, `Line`, `ChainCode`, `IsConvertible`, `HasWarnings`) are satisfied implicitly by the existing public properties.

**Tests:** Extend `InterfaceTests.cs` to verify each entry type is assignable to `IConversionEntry`, that all 7 interface properties work correctly, and that `Diagnostics` returns the correct items through the interface.

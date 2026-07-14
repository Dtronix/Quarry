# Review: 324-migration-honor-naming-mapto

## Classifications
| ID | Class | Rec | Sev | Section | Finding | Action Taken |
|----|-------|-----|-----|---------|---------|--------------|
| F2 | A | A | M | Correctness | NamingStyle detection too permissive vs runtime (no override guard; reads initializer/getter-arrow) → tool/runtime can disagree again | Fixed: `ProjectSchemaReader.cs:113` now gates on `!IsStatic && IsOverride` and reads expression-body member-access only, mirroring `SchemaParser.ExtractNamingStyle`. |
| F7 | A | A | M | Consistency | NamingStyle extraction diverges from runtime SchemaParser acceptance set (same root as F2) | Fixed by the F2 change (tool now accepts exactly the runtime's form set). Extracting a shared cross-project helper was out of scope; exact mirroring achieves parity. |
| F1 | B | B | L | Plan | Step-4 stub decision recorded only in a code comment, not workflow.md Working Notes | Fixed: added a Working Note documenting the real-AccountSchema + Money / stub-UserSchema decision. |
| F4 | B | B | L | Test | NamingStyle path not driven through MigrationCodeGenerator (only MapTo path has the DDL-level assertion) | Fixed: added `NamingStyle_SnakeCase_MigrationCodeUsesStyledColumnNames` asserting the generated migration contains `user_name` and not `UserName`. |
| F5 | B | B | L | Test | No coverage for the divergent parsing forms (getter-arrow/initializer/non-override) or non-literal MapTo | Fixed: added getter-arrow, non-override, and non-literal-MapTo parity tests (all assert Exact / null-MappedName, matching runtime). |
| F8 | B | B | M | Integration | Intended compat break (physical-name diffs/hash shift) not yet documented for users (PR body pending) | To be documented in the PR body Breaking Changes section at PR creation (this step). |
| F3 | D | D | L | Test | Test compilation not checked for diagnostics before extraction | Dismissed: matches the existing `ProjectSchemaReaderIndexTests` harness; assertions target specific physical names that only arise if parsing succeeds, so silent degradation can't produce a false pass. |
| F6 | D | D | L | Test | ReadSampleSource via [CallerFilePath] has no existence guard (environment-coupled) | Dismissed: CI runs `dotnet build` + `dotnet test --no-build` in one workspace with no PathMap/ContinuousIntegrationBuild, so CallerFilePath resolves to the real path; accepted fixture-loading pattern. |

## Plan Compliance
| ID | Finding | Sev | Why It Matters |
| F1 | Step 4 told the author to record the "stub `UserSchema` vs. compile the whole real sample graph" decision in Working Notes at implementation time; it is only captured in a code comment in `ProjectSchemaReaderNamingMapToTests.cs:274-276`, not in `workflow.md` Working Notes. | L | Minor process gap; the rationale is discoverable in the test but the audit trail the plan asked for is missing. |

## Correctness
| ID | Finding | Sev | Why It Matters |
| F2 | NamingStyle detection in `ProjectSchemaReader.cs:113-136` does not faithfully mirror runtime `SchemaParser.ExtractNamingStyle` (`SchemaParser.cs:250-288`): it (a) matches any declared member named `NamingStyle` without the runtime's `!IsStatic && IsOverride` guard, (b) does not validate the property type, and (c) reads `Initializer?.Value` and the getter-arrow body — forms the runtime ignores (runtime only reads `propSyntax.ExpressionBody`). For a legal auto-property-initializer override (`protected override NamingStyle NamingStyle { get; } = NamingStyle.SnakeCase;`) or a getter-arrow body, the tool yields styled names while the runtime yields `Exact`, so the migration emits `user_name` columns the runtime never queries — reintroducing the exact #324 class of mismatch, inverted. | M | Undermines the fix's core guarantee (migration names == runtime physical names) for legal C# forms; the safe design is to mirror the runtime's guard set and expression-body-only parsing exactly. |

## Security
| ID | Finding | Sev | Why It Matters |
No concerns. Inputs are developer-authored schema source parsed via Roslyn; `MapTo`/`NamingStyle` values flow into generated code exactly as `Name`/`MappedName` did before (property names previously, physical names now) — no new external-input or injection surface introduced by this diff.

## Test Quality
| ID | Finding | Sev | Why It Matters |
| F3 | The test compilation (`CreateCompilation`) is never checked for diagnostics before extraction; a missing/incorrect `MetadataReference` would let the semantic model degrade silently (error types still expose `.Name`) rather than fail with a clear cause. | L | A broken reference set could make a test pass for the wrong reason or fail cryptically instead of pinpointing the setup error. |
| F4 | NamingStyle physical-name parity is asserted only at the `TableDef`/`ColumnDef` level (`NamingStyle_SnakeCase_...`); unlike the MapTo/`credit_limit` case, no test drives a snake_case schema through `MigrationCodeGenerator` (the DDL-bound path). | L | A regression in styled-name emission into the actual migration code would go uncaught, since only the MapTo path has the end-to-end `MigrationCodeGenerator` assertion. |
| F5 | No coverage for the parsing forms that actually diverge from the runtime (getter-arrow / auto-property-initializer / non-override `NamingStyle` — see F2) nor for `MapTo` with a non-literal argument (e.g. `MapTo(SomeConst)`). | L | The untested edge cases are precisely where tool/runtime can disagree; happy-path-only coverage hides the divergence. |
| F6 | `ReadSampleSource` locates `../Samples` via `[CallerFilePath]` (`ProjectSchemaReaderNamingMapToTests.cs:266-271`) with no existence guard, so the guard test depends on the compile-time source path still existing at run time. | L | Running the built test assembly on a different machine/path (or with deterministic/path-mapped builds) breaks with `FileNotFoundException`; robust but environment-coupled. |

## Codebase Consistency
| ID | Finding | Sev | Why It Matters |
| F7 | The NamingStyle extraction is a bespoke reimplementation rather than aligning with the runtime `SchemaParser.ExtractNamingStyle` reference impl the plan named as the model; the accepted-forms set and guards differ (see F2), so the two schema readers can disagree. The MapTo/`GenericNameSyntax` changes, by contrast, correctly mirror runtime `GetMethodName`/`ParseColumnModifiers` and follow the file's existing `Computed`/`Collation` else-if idiom. | M | Divergence between the tool and runtime readers is the root cause of #324-style bugs; the two should share guard/parse semantics (ideally the same helper) to stay in lockstep. |

## Integration / Breaking Changes
| ID | Finding | Sev | Why It Matters |
| F8 | Because `ColumnDef.Name` now carries the physical name, the next `migrate add` on any existing project using `NamingStyle` or `MapTo` produces a non-empty diff: column rename-or-drop+add, FK constraint-name shifts (`FK_{table}_{physicalName}` via `colDef?.Name`), and a changed snapshot hash chain (`SchemaHasher` includes `MappedName`). This is the intended behavioral correction, but it is a real compat break in migration lineage; the plan defers the notice to the PR body, which is not yet created (`workflow.md` `pr:` empty). | M | Users must be told their next migration will contain corrective rename/DDL steps and altered hashes; without the PR-body note the break is silent. |

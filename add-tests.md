# Tests to Add

Tests deleted during runtime builder removal that lack carrier-path equivalents.

## Conditional Chain SQL Verification

These tests verified that conditional if/else branching produces the correct SQL output. CrossDialect diagnostics tests only check clause metadata (IsConditional, IsActive), not the actual SQL string.

- **Select_ConditionalWhere_Active_IncludesSql** — Conditional WHERE (active branch) produces SQL with the WHERE clause
- **Select_ConditionalWhere_Inactive_ExcludesSql** — Conditional WHERE (inactive branch) produces SQL without the WHERE clause
- **Select_ConditionalOrderBy_Active_IncludesSql** — Conditional OrderBy (active) includes ORDER BY in SQL
- **Select_ConditionalOrderBy_Inactive_ExcludesSql** — Conditional OrderBy (inactive) omits ORDER BY from SQL
- **Select_MutuallyExclusiveOrderBy_IfBranch** — if/else OrderBy: if-branch column appears in SQL
- **Select_MutuallyExclusiveOrderBy_ElseBranch** — if/else OrderBy: else-branch column appears in SQL
- **Select_ConditionalWhere_CapturedParam_Active** — Conditional WHERE with captured variable parameter, active: param bound
- **Select_ConditionalWhere_CapturedParam_Inactive** — Conditional WHERE with captured variable parameter, inactive: param not bound
- **Delete_ConditionalWhere_Active** — DELETE with conditional WHERE active: produces DELETE...WHERE
- **Delete_ConditionalWhere_Inactive** — DELETE with conditional WHERE inactive: produces DELETE without WHERE
- **Update_SetValue_ConditionalWhere_Active** — UPDATE with conditional WHERE active: produces UPDATE...SET...WHERE
- **Update_SetValue_ConditionalWhere_Inactive** — UPDATE with conditional WHERE inactive: produces UPDATE...SET without WHERE
- **Update_SetAction_ConditionalAdditionalSetAction_Active** — Conditional additional SET clause active: both SET columns present
- **Update_SetAction_ConditionalAdditionalSetAction_Inactive** — Conditional additional SET clause inactive: only base SET column present

## GetColumnTypeName Unit Tests

These tests for `SqlFormatting.GetColumnTypeName()` were collateral deletion (in DialectTypeMappingTests.cs). The function still exists and is used by the schema/migration tooling.

- **{Dialect}_GetColumnTypeName_ShortForm** — CLR type to SQL type mapping for each dialect (int, string, bool, decimal with precision/scale, DateTime, Guid, etc.)
- **{Dialect}_GetColumnTypeName_SystemQualified** — System-qualified type names (System.Int32, System.String) resolve correctly
- **{Dialect}_GetColumnTypeName_PascalCase** — PascalCase type names (Int32, String) resolve correctly
- **AllDialects_ThreeFormConsistency** — Short, Pascal, and System-qualified forms all produce the same SQL type
- **AllDialects_DecimalWithPrecisionNoScale_DefaultsScaleToZero** — decimal(18) defaults to decimal(18,0)
- **{Dialect}_DecimalWithoutPrecision_ReturnsCorrectDefault** — dialect-specific decimal defaults (MySQL/SqlServer: 18,2; SQLite/PG: NUMERIC)
- **AllDialects_UnknownType_ReturnsFallback** — unrecognized CLR type returns TEXT fallback
- **AllDialects_CommonPrimitives_NeverFallThrough** — all common CLR types have explicit mappings (don't hit fallback)

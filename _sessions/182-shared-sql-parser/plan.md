# Plan: Shared SQL Parser (#182)

## Key Concepts

**Recursive-descent parser**: Each grammar rule maps to a method. Operator precedence is encoded via the call hierarchy (e.g., `ParseOr` calls `ParseAnd` calls `ParseComparison` calls `ParseUnary` calls `ParsePrimary`). This style is simple, readable, and debuggable.

**Dialect-aware tokenization**: The tokenizer handles dialect-specific syntax at the token level — parameter prefixes (`@`, `$`, `?`), identifier quoting (backticks, brackets, double-quotes), and boolean literals. The parser itself is mostly dialect-agnostic since the tokenizer normalizes differences.

**Partial AST with unsupported flags**: When the parser encounters constructs it doesn't support (CTEs, window functions, subqueries in FROM), it captures the raw text as an `SqlUnsupported` node and sets a flag on the parse result. This lets consumers quickly check `result.HasUnsupported` to determine convertibility without walking the full tree.

**Expression precedence** (lowest to highest):
1. OR
2. AND
3. NOT (unary prefix)
4. Comparisons: =, <>, !=, <, >, <=, >=, LIKE, IN, BETWEEN, IS NULL, IS NOT NULL
5. Addition/subtraction: +, -
6. Multiplication/division: *, /, %
7. Unary minus: -
8. Primary: literals, identifiers, parameters, function calls, parenthesized expressions, CASE, star

## Implementation Phases

### Phase 1 — Token types and tokenizer (`SqlToken.cs`, `SqlTokenizer.cs`)

Define the `SqlTokenKind` enum covering all token types the parser needs: keywords (SELECT, FROM, WHERE, JOIN, etc.), operators (+, -, *, /, =, <>, etc.), punctuation (comma, dot, open/close paren, semicolon), literals (number, string, parameter), identifiers, and special tokens (EOF, Unknown).

Implement `SqlTokenizer` as a `ref struct` that walks a `ReadOnlySpan<char>` input. It takes `SqlDialect` to configure parameter syntax and identifier quoting. Core method is `NextToken()` which returns the next `SqlToken` (a struct containing `SqlTokenKind`, start offset, and length — text is sliced from the original input span).

The tokenizer handles:
- Whitespace/comment skipping (single-line `--` and block `/* */`)
- Keyword recognition via a simple switch on first character + length (no dictionary allocation)
- String literals with dialect-appropriate escaping (`'it''s'` for all, `\'` for MySQL)
- Numeric literals (integers and decimals, including negative literals)
- Identifier quoting: `"id"` (ANSI), `` `id` `` (MySQL), `[id]` (SqlServer)
- Parameters: `@name` (SQLite/SqlServer), `$n` (PostgreSQL), `?` (MySQL)

Note: `ref struct` tokenizer avoids allocations — it works on the caller's span. Since the parser will need to backtrack/peek, it will store tokens in a list rather than consuming the tokenizer lazily.

Tests for Phase 1:
- Tokenize simple SELECT statement, verify token sequence
- Tokenize each dialect's parameter syntax
- Tokenize quoted identifiers per dialect
- Tokenize string literals with escaped quotes
- Tokenize numeric literals (integer, decimal)
- Tokenize operators and punctuation
- Verify comments are skipped
- Edge cases: empty input, unknown characters produce Unknown tokens

### Phase 2 — AST node hierarchy (`SqlNode.cs`)

Define the AST node classes. All nodes inherit from `SqlNode` (abstract base). The hierarchy:

**Statement level:**
- `SqlSelectStatement` — IsDistinct, Columns, From, Where, GroupBy, Having, OrderBy, Limit, Offset
- `SqlUnsupported` — RawText (captures text of unsupported constructs like CTEs, UNION, window functions)

**Select columns:**
- `SqlSelectColumn` — Expression + optional Alias
- `SqlStarColumn` — represents `*` or `table.*`

**Table sources:**
- `SqlTableSource` — TableName, Schema, Alias
- `SqlJoin` — JoinKind (Inner/Left/Right/Cross/FullOuter), Table (SqlTableSource), Condition (SqlExpr)

**Expressions (all extend `SqlExpr` base):**
- `SqlBinaryExpr` — Left, Operator (SqlBinaryOp enum), Right
- `SqlUnaryExpr` — Operator (NOT, unary minus), Operand
- `SqlColumnRef` — optional TableAlias, ColumnName
- `SqlLiteral` — Value (string representation), LiteralKind (String/Number/Boolean/Null)
- `SqlParameter` — RawText (e.g., "@userId", "$1", "?")
- `SqlFunctionCall` — FunctionName, Arguments, optional IsDistinct (for COUNT(DISTINCT x))
- `SqlInExpr` — Expression, Values list, IsNegated
- `SqlBetweenExpr` — Expression, Low, High, IsNegated
- `SqlIsNullExpr` — Expression, IsNegated (IS NOT NULL)
- `SqlParenExpr` — Inner expression
- `SqlCaseExpr` — optional Operand, WhenClauses (condition + result pairs), ElseResult
- `SqlCastExpr` — Expression, TypeName
- `SqlExistsExpr` — Subquery (SqlSelectStatement)

**Order:**
- `SqlOrderTerm` — Expression, IsDescending

**Enums:**
- `SqlBinaryOp` — Equal, NotEqual, LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual, Like, And, Or, Add, Subtract, Multiply, Divide, Modulo
- `SqlJoinKind` — Inner, Left, Right, Cross, FullOuter

Each class is `internal sealed`, has a constructor initializing get-only properties, and implements `IEquatable<T>` with `Equals` and `GetHashCode`. The base `SqlNode` class provides an abstract `NodeKind` property (enum) for fast type checking without `is` casts.

Tests for Phase 2: No standalone tests — AST nodes are exercised through parser tests in Phase 4.

### Phase 3 — Recursive-descent parser (`SqlParser.cs`, `SqlParseResult.cs`)

`SqlParseResult` is a simple container: `SqlSelectStatement? Statement`, `IReadOnlyList<SqlParseDiagnostic> Diagnostics`, `bool HasUnsupported`. `SqlParseDiagnostic` holds position, length, and message.

`SqlParser` is an `internal sealed class` that takes a token list (from Phase 1) and the original SQL string. Public entry point: `static SqlParseResult Parse(string sql, SqlDialect dialect)`.

Internal flow:
1. Tokenize the full input into a `List<SqlToken>` using `SqlTokenizer`
2. Parse using recursive descent with a position cursor

Parser methods (mapping to grammar rules):
- `ParseSelectStatement()` → handles SELECT [DISTINCT] columns FROM ... [WHERE] [GROUP BY] [HAVING] [ORDER BY] [LIMIT] [OFFSET]
- `ParseSelectColumns()` → comma-separated list of expressions with optional aliases, or `*`
- `ParseFromClause()` → table name with optional alias
- `ParseJoins()` → zero or more JOIN clauses
- `ParseJoin()` → [INNER|LEFT|RIGHT|CROSS|FULL] [OUTER] JOIN table ON condition
- `ParseWhereClause()` → WHERE expression
- `ParseGroupByClause()` → GROUP BY expr, expr, ...
- `ParseHavingClause()` → HAVING expression
- `ParseOrderByClause()` → ORDER BY term, term, ... each is expr [ASC|DESC]
- `ParseLimitOffset()` → dialect-aware: LIMIT n [OFFSET n] or OFFSET n ROWS FETCH NEXT n ROWS ONLY

Expression parsing (precedence climbing):
- `ParseExpression()` → `ParseOrExpr()`
- `ParseOrExpr()` → `ParseAndExpr()` (OR `ParseAndExpr()`)*
- `ParseAndExpr()` → `ParseNotExpr()` (AND `ParseNotExpr()`)*
- `ParseNotExpr()` → NOT? `ParseComparisonExpr()`
- `ParseComparisonExpr()` → `ParseAddExpr()` ([=|<>|!=|<|>|<=|>=|LIKE] `ParseAddExpr()` | IN (...) | BETWEEN ... AND ... | IS [NOT] NULL)*
- `ParseAddExpr()` → `ParseMulExpr()` ([+|-] `ParseMulExpr()`)*
- `ParseMulExpr()` → `ParseUnaryExpr()` ([*|/|%] `ParseUnaryExpr()`)*
- `ParseUnaryExpr()` → [-]? `ParsePrimaryExpr()`
- `ParsePrimaryExpr()` → literal | parameter | identifier (possibly function call if followed by `(`) | `(` subexpr `)` | CASE | CAST | EXISTS

CTE detection: If the first token is `WITH`, emit `SqlUnsupported` for the entire statement.
Subquery detection: If `(` is followed by `SELECT`, parse as `SqlExistsExpr` if preceded by EXISTS, otherwise emit `SqlUnsupported`.
Window function detection: If `OVER` follows a function call close paren, emit `SqlUnsupported` for that expression.

Tests for Phase 3:
- Parse basic `SELECT col FROM table` 
- Parse `SELECT a, b, c FROM t WHERE x = 1`
- Parse `SELECT * FROM t`
- Parse `SELECT t.* FROM t`
- Parse `SELECT DISTINCT a FROM t`
- Parse joins (INNER, LEFT, RIGHT, CROSS, FULL OUTER) with ON conditions
- Parse WHERE with AND/OR/NOT combinations
- Parse operator precedence: `a = 1 AND b = 2 OR c = 3` → OR(AND(=, =), =)
- Parse IN expression: `x IN (1, 2, 3)` and `x NOT IN (...)`
- Parse BETWEEN: `x BETWEEN 1 AND 10`
- Parse IS NULL / IS NOT NULL
- Parse LIKE
- Parse GROUP BY + HAVING
- Parse ORDER BY with ASC/DESC
- Parse LIMIT/OFFSET (SQLite/PostgreSQL/MySQL style)
- Parse OFFSET FETCH (SqlServer style)
- Parse function calls: `COUNT(*)`, `COUNT(DISTINCT x)`, `COALESCE(a, b)`
- Parse nested expressions with parens
- Parse CASE WHEN expressions
- Parse CAST expressions
- Parse aliased columns: `SELECT a AS alias`
- Parse aliased tables: `FROM users u`
- Parse parameters per dialect: `@p0`, `$1`, `?`
- Parse qualified columns: `u.name`
- Parse schema-qualified tables: `public.users`
- Unsupported: CTE (WITH ... AS) → SqlUnsupported, HasUnsupported = true
- Unsupported: Window function (OVER) → SqlUnsupported
- Unsupported: Subquery in FROM → SqlUnsupported
- Error recovery: malformed SQL produces diagnostics but doesn't throw

### Phase 4 — Project wiring

Wire the parser into the build:
1. `Quarry.csproj`: Add `<Compile Remove>` for `Sql/Parser/**` to exclude parser from runtime
2. `Quarry.Tests.csproj`: Verify parser types are accessible via Generator assembly (no changes expected — InternalsVisibleTo already in place)
3. No changes to `Quarry.Generator.csproj` or `Quarry.Analyzers.csproj` — they already include `Sql/**/*.cs` via the shared projitems wildcard

Tests for Phase 4:
- Verify the full test suite still passes (no compilation errors from project wiring)
- All parser tests from Phase 1 and Phase 3 pass

### Phase 5 — Integration verification and edge case hardening

Round-trip validation: for each supported SQL pattern, parse and verify the AST captures all semantic information needed by the three intended consumers (Generator, Analyzers, Migration).

Add edge case tests:
- Multiple joins in sequence
- Deeply nested expressions
- Empty/whitespace-only input
- SQL with trailing semicolons
- Mixed-case keywords (SeLeCt)
- Identifiers that clash with keywords (column named "select" must be quoted)
- String literals containing SQL keywords
- Numeric edge cases (leading zeros, very large numbers)
- Multiple tables in FROM (comma-separated implicit cross join)

Verify all existing tests still pass.

## Dependencies Between Phases

Phase 1 (tokens/tokenizer) → Phase 2 (AST nodes) → Phase 3 (parser) → Phase 4 (wiring) → Phase 5 (hardening)

Phases 1 and 2 could technically be done in parallel since they're independent, but the commit sequence is cleaner if Phase 1 comes first (tokenizer is the lowest-level component).

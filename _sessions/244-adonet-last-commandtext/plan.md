# Plan: 244-adonet-last-commandtext

## Key Concepts

**Span-based positional filtering**: Roslyn `SyntaxNode.Span` provides the text span (start/end offset) of each node in the source. By comparing the span end of a `CommandText` assignment against the span start of the `Execute*` invocation, we can filter to only assignments that appear before the call, then pick the last one.

## Phase 1: Fix FindCommandTextAssignment

**File**: `src/Quarry.Migration/AdoNetDetector.cs`

1. Change `FindCommandTextAssignment` signature to accept an additional `InvocationExpressionSyntax executeInvocation` parameter.
2. Replace the early-return-on-first-match loop with logic that:
   - Iterates all `AssignmentExpressionSyntax` descendants (same as today).
   - For each matching `commandVar.CommandText = ...` assignment, checks that the assignment's `Span.End` is less than `executeInvocation.SpanStart`.
   - Tracks the last such match (by overwriting a local variable on each qualifying hit).
3. After the loop, attempt `ExtractStringValue` on the last match and return the result (or null if no match found).
4. Update the call site at line 88 to pass `invocation` as the new argument.

**Tests to add** (`src/Quarry.Migration.Tests/AdoNetDetectorTests.cs`):
- `Detect_ReassignedCommandText_UsesLastBeforeExecute`: Two `CommandText` assignments followed by one `ExecuteReader` call. Assert that the SQL from the second (last) assignment is detected.
- `Detect_CommandTextAfterExecute_Ignored`: A `CommandText` assignment before `Execute`, then another after. Assert only the before-assignment's SQL is used.

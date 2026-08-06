## Description

The generator predicts compiler-generated display-class names (`<>c__DisplayClass{M}_{C}`) in order to
emit `[UnsafeAccessor]` extractors. For at least one shape, the **closure ordinal** the C# compiler
assigns differs between compiler versions, so a prediction that is correct on one SDK is wrong on
another — with no build-time signal. The chain compiles and throws on first execution:

```
System.TypeLoadException : Could not resolve type
  'Quarry.Tests.Integration.ConcurrencyTests+<>c__DisplayClass5_3' in assembly 'Quarry.Tests'
```

Same source, same repo, same commit:

| Environment | SDK | Predicted | Actually emitted |
|---|---|---|---|
| Local (Windows) | 10.0.110 | `<>c__DisplayClass5_3` | `<>c__DisplayClass5_3` — passes |
| CI (ubuntu-latest) | 10.0.302 | `<>c__DisplayClass5_3` | `<>c__DisplayClass5_1` — `TypeLoadException` |

The **method** ordinal (`5`) matched on both; only the **closure** ordinal diverged.

## Location

`src/Quarry.Generator/Parsing/DisplayClassNameResolver.cs` — `AnalyzeMethodClosures` /
`AssignOrdinalsPreOrder`, which number capture scopes by a purely syntactic pre-order walk.

## Diagnostics

`System.TypeLoadException : Could not resolve type '…+<>c__DisplayClass{M}_{C}'` thrown from a generated
`__ExtractVar_*` accessor at first execution of the chain.

The affected shape is an **async lambda inside a loop, whose clause captures a local**:

```csharp
for (int i = 0; i < Workers; i++)
{
    var index = i;
    tasks[i] = Task.Run(async () =>
    {
        var (Lite, _, _, _) = harnesses[index];
        var name = $"Worker{index}";
        await Lite.Users().Update().Set(u => u.UserName = name)   // `name` extraction fails
            .Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    });
}
```

Note a sibling test with the same lambda nesting but a clause that captures **nothing**
(`o => o.Total > 100.00m`) is unaffected — no extraction, so no display class is named.

## What Has Been Tried

- **Confirmed it is not a source difference.** The failure reproduces from the same commit; the only
  variable is the SDK. Reverting the fixture to named worker methods (making the captures ordinary method
  locals) makes it pass on both.
- **Confirmed the simpler shapes are stable.** `Generation/LambdaCaptureScopeTests` and
  `Generation/LambdaCaptureExecutionTests` — covering chains inside single and nested lambdas, `foreach` /
  `for` / `using` / `switch`-section / `catch` scopes, and instance fields mixed with locals — pass on
  **both** SDKs. Only the async-lambda-in-a-loop shape diverges.
- **Confirmed the method ordinal is not implicated**, only the closure ordinal.

## Gathered Information

- The repo has no `global.json`, so CI resolves `10.0.x` to whatever is current on the runner while
  developers build with whatever they have installed. That makes the divergence a moving target: the same
  branch can pass locally and fail in CI, or vice versa, with no code change.
- Async lambdas are the plausible trigger. The compiler rewrites an async lambda into a state machine, and
  locals captured by a nested lambda are hoisted into display classes whose numbering relative to the
  surrounding loop/method scopes is an implementation detail that no documented rule fixes.
- Ground truth for the shapes that *are* stable is tabulated in the "Display Class Prediction" section of
  `src/Quarry.Generator/llm.md`.
- Related but distinct: #310 (prediction robustness for cross-partial ordinal shifts and generic
  containing types) and #339 (multi-scope captures, upstream-blocked). This issue is about the same
  prediction being version-dependent for a fixed source shape.

## Suggested Approach

Ordered by preference:

1. **Stop guessing the ordinal for this shape.** Detect an async lambda between the clause and its
   containing method and disqualify with a QRY diagnostic, the way multi-scope captures already are.
   A build error naming the shape beats a `TypeLoadException` in production.
2. **Verify the prediction at runtime, cheaply.** The generated carrier could fail fast with a message
   naming the predicted vs. actual display class instead of a bare `TypeLoadException` — this took
   a full CI cycle to diagnose from the raw exception.
3. **Pin the SDK with `global.json`** so the repo at least fails consistently. This narrows the blast
   radius for contributors but does nothing for consumers, who compile with their own SDK — so it is a
   mitigation, not a fix.
4. Longer term, the whole prediction approach is exposed to this class of break. If
   [dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664) ever makes closure fields
   readable without prediction, that removes the root cause.

Add a regression test that asserts the shape either works or is rejected — it must **execute**, since a
wrong prediction still compiles.

namespace Quarry;

/// <summary>
/// Delegate used by the lambda form of <c>Update().Set(...)</c> for partial updates.
/// The lambda receives the entity's generated <c>Patch</c> struct by reference and
/// runs verbatim — full C# semantics, no expression-tree reconstruction. The Patch
/// struct's write-tracking setters record which fields were assigned, so the runtime
/// SET clause includes only the touched columns.
/// </summary>
/// <typeparam name="T">The generated Patch struct type (e.g. <c>User.Patch</c>). Inferred from the
/// <c>Set</c> overload's parameter signature — callers do not need to specify it explicitly.</typeparam>
/// <param name="patch">The Patch value being mutated. Passed by reference so setter calls on the
/// generated Patch struct update the caller-visible value, including its <c>__mask</c> field.</param>
public delegate void PatchAction<T>(ref T patch);

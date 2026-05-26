namespace Quarry;

/// <summary>
/// Marker interface implemented by generated <c>Entity.Patch</c> nested structs.
/// Used as a generic constraint on the partial-update <see cref="IUpdateBuilder{T}.Set{TPatch}(TPatch)"/>
/// (and <see cref="IExecutableUpdateBuilder{T}.Set{TPatch}(TPatch)"/>) overloads so the C# compiler
/// rejects cross-entity patches at the call site (e.g. passing a <c>User.Patch</c> to
/// <c>db.Orders().Update().Set(...)</c>).
/// </summary>
/// <typeparam name="T">The entity type the Patch struct mutates.</typeparam>
public interface IPatchFor<T> where T : class
{
}

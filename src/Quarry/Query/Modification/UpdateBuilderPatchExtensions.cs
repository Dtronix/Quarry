namespace Quarry;

/// <summary>
/// Partial-update <c>Set</c> overloads for <see cref="IUpdateBuilder{T}"/> and
/// <see cref="IExecutableUpdateBuilder{T}"/>. These are extension methods (not
/// interface DIMs) so they don't disturb overload resolution and interceptor
/// binding for the existing non-generic <c>Set(T entity)</c> and
/// <c>Set(Action&lt;T&gt;)</c> instance methods on the builder interfaces.
/// </summary>
/// <remarks>
/// Each method just throws — like the carrier-method DIMs on the builder
/// interfaces, the bodies are placeholders the source generator replaces with
/// real interceptor methods at every call site. Hitting the throw indicates
/// the chain was not analyzed for compile-time emission.
/// </remarks>
public static class UpdateBuilderPatchExtensions
{
    /// <summary>Apply a partial update by setting the columns whose mask bits are set on <paramref name="patch"/>.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TPatch">The generated <c>Entity.Patch</c> struct for <typeparamref name="T"/>; inferred at the call site.</typeparam>
    public static IUpdateBuilder<T> Set<T, TPatch>(this IUpdateBuilder<T> builder, TPatch patch)
        where T : class
        where TPatch : struct, IPatchFor<T>
        => throw new InvalidOperationException("Extension Set(Patch) on IUpdateBuilder is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>Apply a partial update by invoking a builder lambda that mutates a Patch by reference.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TPatch">The generated <c>Entity.Patch</c> struct for <typeparamref name="T"/>; inferred at the call site.</typeparam>
    public static IUpdateBuilder<T> Set<T, TPatch>(this IUpdateBuilder<T> builder, PatchAction<TPatch> action)
        where T : class
        where TPatch : struct, IPatchFor<T>
        => throw new InvalidOperationException("Extension Set(PatchAction) on IUpdateBuilder is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>Apply a partial update by setting the columns whose mask bits are set on <paramref name="patch"/>.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TPatch">The generated <c>Entity.Patch</c> struct for <typeparamref name="T"/>; inferred at the call site.</typeparam>
    public static IExecutableUpdateBuilder<T> Set<T, TPatch>(this IExecutableUpdateBuilder<T> builder, TPatch patch)
        where T : class
        where TPatch : struct, IPatchFor<T>
        => throw new InvalidOperationException("Extension Set(Patch) on IExecutableUpdateBuilder is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>Apply a partial update by invoking a builder lambda that mutates a Patch by reference.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TPatch">The generated <c>Entity.Patch</c> struct for <typeparamref name="T"/>; inferred at the call site.</typeparam>
    public static IExecutableUpdateBuilder<T> Set<T, TPatch>(this IExecutableUpdateBuilder<T> builder, PatchAction<TPatch> action)
        where T : class
        where TPatch : struct, IPatchFor<T>
        => throw new InvalidOperationException("Extension Set(PatchAction) on IExecutableUpdateBuilder is not intercepted in this optimized chain. This indicates a code generation bug.");
}

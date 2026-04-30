using System.Collections.Generic;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Records, per carrier class, which Px parameter-field indices were assigned
/// during interceptor body emission. Used by <see cref="FileEmitter"/> to
/// enforce the QRY037 invariant: every carrier P-field must be assigned by at
/// least one emitted interceptor body. Without this self-check, silent
/// default(T) parameter binding can ship — CS0649 covers only value-type and
/// nullable-ref-type fields; non-nullable reference-type P-fields carry a
/// `= null!` initializer that suppresses CS0649.
/// </summary>
internal sealed class CarrierAssignmentRecorder
{
    private readonly Dictionary<string, HashSet<int>> _assigned = new();

    /// <summary>
    /// Records that the interceptor body just emitted contains an
    /// <c>__c.P{pIndex} = ...</c> assignment for the given carrier.
    /// </summary>
    public void Record(string carrierClassName, int pIndex)
    {
        if (!_assigned.TryGetValue(carrierClassName, out var set))
        {
            set = new HashSet<int>();
            _assigned[carrierClassName] = set;
        }
        set.Add(pIndex);
    }

    /// <summary>
    /// Returns the set of P-indices assigned for the given carrier, or an
    /// empty set if no assignments have been recorded.
    /// </summary>
    public HashSet<int> GetAssigned(string carrierClassName) =>
        _assigned.TryGetValue(carrierClassName, out var set) ? set : new HashSet<int>();
}

namespace Quarry.Shared.Migration;

/// <summary>
/// Classifies migration steps by their risk level.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
enum StepClassification
{
    Safe,
    Cautious,
    Destructive
}

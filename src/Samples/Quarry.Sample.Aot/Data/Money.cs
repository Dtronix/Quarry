namespace Quarry.Sample.Aot.Data;

public readonly struct Money(decimal amount)
{
    public decimal Amount { get; } = amount;
    public override string ToString() => $"${Amount:F2}";
}

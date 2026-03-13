using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// A value type representing a monetary amount, used to test TypeMapping integration.
/// </summary>
public readonly struct Money
{
    public decimal Amount { get; }

    public Money(decimal amount) => Amount = amount;

    public override string ToString() => $"${Amount:F2}";

    public override bool Equals(object? obj) => obj is Money m && m.Amount == Amount;
    public override int GetHashCode() => Amount.GetHashCode();

    public static bool operator ==(Money left, Money right) => left.Amount == right.Amount;
    public static bool operator !=(Money left, Money right) => left.Amount != right.Amount;
}

/// <summary>
/// Maps Money (custom CLR type) to decimal (database type).
/// </summary>
public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}

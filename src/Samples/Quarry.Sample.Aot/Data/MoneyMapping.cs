using Quarry;

namespace Quarry.Sample.Aot.Data;

public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}

using Quarry.Tests.Samples;

namespace Quarry.Tests;

/// <summary>
/// Tests for the runtime TypeMapping registry used by the fallback execution path.
/// </summary>
[TestFixture]
internal class TypeMappingRegistryTests
{
    [Test]
    public void MoneyMapping_IsAutoRegistered_WhenInstantiated()
    {
        // Instantiating a TypeMapping subclass should auto-register it
        _ = new MoneyMapping();

        var money = new Money(42.50m);
        var result = TypeMappingRegistry.TryConvert(typeof(Money), money, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(42.50m));
        Assert.That(converted!.GetType(), Is.EqualTo(typeof(decimal)));
    }

    [Test]
    public void TryConvert_ReturnsFalse_ForUnmappedType()
    {
        var result = TypeMappingRegistry.TryConvert(typeof(string), "hello", out var converted);

        Assert.That(result, Is.False);
        Assert.That(converted, Is.Null);
    }

    [Test]
    public void TryConvert_ReturnsFalse_ForPrimitiveTypes()
    {
        // Primitive types should not be converted, even after mappings are registered
        _ = new MoneyMapping();

        var result = TypeMappingRegistry.TryConvert(typeof(int), 42, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void NormalizeParameterValue_ConvertsMappedType()
    {
        // Simulate the full NormalizeParameterValue flow for a Money value
        _ = new MoneyMapping();

        object value = new Money(99.95m);
        var type = value.GetType();

        // Not null, not enum — should hit the TypeMappingRegistry path
        Assert.That(type.IsEnum, Is.False);
        Assert.That(TypeMappingRegistry.TryConvert(type, value, out var converted), Is.True);
        Assert.That(converted, Is.EqualTo(99.95m));
    }
}

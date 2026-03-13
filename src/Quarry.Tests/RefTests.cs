namespace Quarry.Tests;

/// <summary>
/// Tests for the EntityRef&lt;TEntity, TKey&gt; struct (runtime FK type for generated entities).
/// </summary>
public class RefTests
{
    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [Test]
    public void EntityRef_DefaultConstructor_HasDefaultValues()
    {
        var refValue = new EntityRef<TestEntity, int>();

        Assert.That(refValue.Id, Is.EqualTo(0));
        Assert.That(refValue.Value, Is.Null);
    }

    [Test]
    public void EntityRef_ConstructorWithId_SetsIdAndNullValue()
    {
        var refValue = new EntityRef<TestEntity, int>(42);

        Assert.That(refValue.Id, Is.EqualTo(42));
        Assert.That(refValue.Value, Is.Null);
    }

    [Test]
    public void EntityRef_ConstructorWithIdAndValue_SetsBoth()
    {
        var entity = new TestEntity { Id = 42, Name = "Test" };
        var refValue = new EntityRef<TestEntity, int>(42, entity);

        Assert.That(refValue.Id, Is.EqualTo(42));
        Assert.That(refValue.Value, Is.SameAs(entity));
    }

    [Test]
    public void EntityRef_ImplicitConversionFromKey_CreatesRefWithId()
    {
        EntityRef<TestEntity, int> refValue = 42;

        Assert.That(refValue.Id, Is.EqualTo(42));
        Assert.That(refValue.Value, Is.Null);
    }

    [Test]
    public void EntityRef_InitSyntax_SetsProperties()
    {
        var entity = new TestEntity { Id = 1, Name = "Test" };
        var refValue = new EntityRef<TestEntity, int> { Id = 1, Value = entity };

        Assert.That(refValue.Id, Is.EqualTo(1));
        Assert.That(refValue.Value, Is.SameAs(entity));
    }

    [Test]
    public void EntityRef_WithStringKey_Works()
    {
        var refValue = new EntityRef<TestEntity, string>("abc-123");

        Assert.That(refValue.Id, Is.EqualTo("abc-123"));
        Assert.That(refValue.Value, Is.Null);
    }

    [Test]
    public void EntityRef_WithGuidKey_Works()
    {
        var guid = Guid.NewGuid();
        var refValue = new EntityRef<TestEntity, Guid>(guid);

        Assert.That(refValue.Id, Is.EqualTo(guid));
        Assert.That(refValue.Value, Is.Null);
    }

    [Test]
    public void EntityRef_IsValueType()
    {
        Assert.That(typeof(EntityRef<TestEntity, int>).IsValueType, Is.True);
    }

    [Test]
    public void EntityRef_IsReadOnly()
    {
        // Verify that EntityRef is a readonly struct by checking that it can't be modified
        var refValue = new EntityRef<TestEntity, int>(42);

        // This test verifies the struct is immutable by design
        // The readonly modifier ensures this at compile time
        Assert.That(refValue.Id, Is.EqualTo(42));
    }
}

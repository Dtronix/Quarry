namespace Quarry.Tests;

/// <summary>
/// Tests for column type instantiation, implicit conversions, and fluent builder API.
/// </summary>
public class ColumnTypeTests
{
    private class DummySchema : Schema
    {
        public static string Table => "dummy";
    }

    [Test]
    public void Col_IsValueType()
    {
        Assert.That(typeof(Col<int>).IsValueType, Is.True);
    }

    [Test]
    public void Key_IsValueType()
    {
        Assert.That(typeof(Key<int>).IsValueType, Is.True);
    }

    [Test]
    public void Many_IsValueType()
    {
        Assert.That(typeof(Many<DummySchema>).IsValueType, Is.True);
    }

    [Test]
    public void Col_ImplicitFromColumnBuilder_Compiles()
    {
        Col<int> col = new ColumnBuilder<int>().Identity();

        Assert.That(col, Is.EqualTo(default(Col<int>)));
    }

    [Test]
    public void Key_ImplicitFromColumnBuilder_Compiles()
    {
        Key<int> key = new ColumnBuilder<int>().Identity();

        Assert.That(key, Is.EqualTo(default(Key<int>)));
    }

    [Test]
    public void ColumnBuilder_Length_ReturnsDefault()
    {
        var builder = new ColumnBuilder<string>().Length(100);

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<string>)));
    }

    [Test]
    public void ColumnBuilder_Precision_ReturnsDefault()
    {
        var builder = new ColumnBuilder<decimal>().Precision(18, 2);

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<decimal>)));
    }

    [Test]
    public void ColumnBuilder_Identity_ReturnsDefault()
    {
        var builder = new ColumnBuilder<int>().Identity();

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<int>)));
    }

    [Test]
    public void ColumnBuilder_ClientGenerated_ReturnsDefault()
    {
        var builder = new ColumnBuilder<Guid>().ClientGenerated();

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<Guid>)));
    }

    [Test]
    public void ColumnBuilder_Computed_ReturnsDefault()
    {
        var builder = new ColumnBuilder<decimal>().Computed();

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<decimal>)));
    }

    [Test]
    public void ColumnBuilder_Default_ReturnsDefault()
    {
        var builder = new ColumnBuilder<bool>().Default(true);

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<bool>)));
    }

    [Test]
    public void ColumnBuilder_DefaultFactory_ReturnsDefault()
    {
        var builder = new ColumnBuilder<DateTime>().Default(() => DateTime.UtcNow);

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<DateTime>)));
    }

    [Test]
    public void ColumnBuilder_MapTo_ReturnsDefault()
    {
        var builder = new ColumnBuilder<string>().MapTo("user_name");

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<string>)));
    }

    [Test]
    public void ColumnBuilder_ChainedModifiers_ReturnsDefault()
    {
        var builder = new ColumnBuilder<string>()
            .Length(100)
            .MapTo("custom_name");

        Assert.That(builder, Is.EqualTo(default(ColumnBuilder<string>)));
    }

    [Test]
    public void RefBuilder_ForeignKey_ReturnsDefault()
    {
        var builder = new RefBuilder<DummySchema, int>().ForeignKey();

        Assert.That(builder, Is.EqualTo(default(RefBuilder<DummySchema, int>)));
    }

    [Test]
    public void RefBuilder_MapTo_ReturnsDefault()
    {
        var builder = new RefBuilder<DummySchema, int>().MapTo("fk_column");

        Assert.That(builder, Is.EqualTo(default(RefBuilder<DummySchema, int>)));
    }
}

using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class ColumnBuilderTests
{
    [Test]
    public void Build_ClrTypeOnly_SetsClrType()
    {
        var builder = new ColumnBuilder();
        builder.ClrType("int");
        var def = builder.Build();
        Assert.That(def.ClrType, Is.EqualTo("int"));
        Assert.That(def.SqlType, Is.Null);
    }

    [Test]
    public void Build_SqlTypeOnly_SetsSqlType()
    {
        var builder = new ColumnBuilder();
        builder.Type("NVARCHAR(100)");
        var def = builder.Build();
        Assert.That(def.SqlType, Is.EqualTo("NVARCHAR(100)"));
        Assert.That(def.ClrType, Is.Null);
    }

    [Test]
    public void Build_LengthSetsMaxLength()
    {
        var builder = new ColumnBuilder();
        builder.Length(255);
        var def = builder.Build();
        Assert.That(def.MaxLength, Is.EqualTo(255));
    }

    [Test]
    public void Build_PrecisionAndScale_SetsBoth()
    {
        var builder = new ColumnBuilder();
        builder.Precision(18, 2);
        var def = builder.Build();
        Assert.That(def.Precision, Is.EqualTo(18));
        Assert.That(def.Scale, Is.EqualTo(2));
    }

    [Test]
    public void Build_Nullable_SetsIsNullableTrue()
    {
        var builder = new ColumnBuilder();
        builder.Nullable();
        var def = builder.Build();
        Assert.That(def.IsNullable, Is.True);
    }

    [Test]
    public void Build_NotNull_SetsIsNullableFalse()
    {
        var builder = new ColumnBuilder();
        builder.NotNull();
        var def = builder.Build();
        Assert.That(def.IsNullable, Is.False);
    }

    [Test]
    public void Build_NullableThenNotNull_LastWins_NotNullable()
    {
        var builder = new ColumnBuilder();
        builder.Nullable().NotNull();
        var def = builder.Build();
        Assert.That(def.IsNullable, Is.False);
    }

    [Test]
    public void Build_Identity_SetsIsIdentity()
    {
        var builder = new ColumnBuilder();
        builder.Identity();
        var def = builder.Build();
        Assert.That(def.IsIdentity, Is.True);
    }

    [Test]
    public void Build_DefaultValue_SetsDefaultValue()
    {
        var builder = new ColumnBuilder();
        builder.DefaultValue("'hello'");
        var def = builder.Build();
        Assert.That(def.DefaultValue, Is.EqualTo("'hello'"));
    }

    [Test]
    public void Build_DefaultExpression_SetsDefaultExpression()
    {
        var builder = new ColumnBuilder();
        builder.DefaultExpression("GETDATE()");
        var def = builder.Build();
        Assert.That(def.DefaultExpression, Is.EqualTo("GETDATE()"));
    }

    [Test]
    public void Build_AllProperties_Combined()
    {
        var builder = new ColumnBuilder();
        builder.Type("DECIMAL").ClrType("decimal").Length(50).Precision(10, 4)
            .Nullable().Identity().DefaultValue("0").DefaultExpression("GETDATE()");
        var def = builder.Build();
        Assert.That(def.SqlType, Is.EqualTo("DECIMAL"));
        Assert.That(def.ClrType, Is.EqualTo("decimal"));
        Assert.That(def.MaxLength, Is.EqualTo(50));
        Assert.That(def.Precision, Is.EqualTo(10));
        Assert.That(def.Scale, Is.EqualTo(4));
        Assert.That(def.IsNullable, Is.True);
        Assert.That(def.IsIdentity, Is.True);
        Assert.That(def.DefaultValue, Is.EqualTo("0"));
        Assert.That(def.DefaultExpression, Is.EqualTo("GETDATE()"));
    }

    [Test]
    public void Build_NoProperties_AllDefaults()
    {
        var builder = new ColumnBuilder();
        var def = builder.Build();
        Assert.That(def.SqlType, Is.Null);
        Assert.That(def.ClrType, Is.Null);
        Assert.That(def.MaxLength, Is.Null);
        Assert.That(def.Precision, Is.Null);
        Assert.That(def.Scale, Is.Null);
        Assert.That(def.IsNullable, Is.False);
        Assert.That(def.IsIdentity, Is.False);
        Assert.That(def.DefaultValue, Is.Null);
        Assert.That(def.DefaultExpression, Is.Null);
    }
}

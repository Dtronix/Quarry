using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class MigrationStepClassifyTests
{
    [Test]
    public void Classify_CreateTable_ReturnsSafe()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.CreateTable), Is.EqualTo(StepClassification.Safe));
    }

    [Test]
    public void Classify_AddIndex_ReturnsSafe()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.AddIndex), Is.EqualTo(StepClassification.Safe));
    }

    [Test]
    public void Classify_AddForeignKey_ReturnsSafe()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.AddForeignKey), Is.EqualTo(StepClassification.Safe));
    }

    [Test]
    public void Classify_AddColumn_Nullable_ReturnsSafe()
    {
        var col = new ColumnDef("test", "string", isNullable: true, kind: ColumnKind.Standard);
        Assert.That(MigrationStep.Classify(MigrationStepType.AddColumn, col), Is.EqualTo(StepClassification.Safe));
    }

    [Test]
    public void Classify_AddColumn_NonNullWithDefault_ReturnsSafe()
    {
        var col = new ColumnDef("test", "string", isNullable: false, kind: ColumnKind.Standard, hasDefault: true);
        Assert.That(MigrationStep.Classify(MigrationStepType.AddColumn, col), Is.EqualTo(StepClassification.Safe));
    }

    [Test]
    public void Classify_AddColumn_NonNullNoDefault_ReturnsDestructive()
    {
        var col = new ColumnDef("test", "string", isNullable: false, kind: ColumnKind.Standard, hasDefault: false);
        Assert.That(MigrationStep.Classify(MigrationStepType.AddColumn, col), Is.EqualTo(StepClassification.Destructive));
    }

    [Test]
    public void Classify_AlterColumn_ReturnsCautious()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.AlterColumn), Is.EqualTo(StepClassification.Cautious));
    }

    [Test]
    public void Classify_RenameTable_ReturnsCautious()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.RenameTable), Is.EqualTo(StepClassification.Cautious));
    }

    [Test]
    public void Classify_RenameColumn_ReturnsCautious()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.RenameColumn), Is.EqualTo(StepClassification.Cautious));
    }

    [Test]
    public void Classify_DropTable_ReturnsDestructive()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.DropTable), Is.EqualTo(StepClassification.Destructive));
    }

    [Test]
    public void Classify_DropColumn_ReturnsDestructive()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.DropColumn), Is.EqualTo(StepClassification.Destructive));
    }

    [Test]
    public void Classify_DropIndex_ReturnsDestructive()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.DropIndex), Is.EqualTo(StepClassification.Destructive));
    }

    [Test]
    public void Classify_DropForeignKey_ReturnsDestructive()
    {
        Assert.That(MigrationStep.Classify(MigrationStepType.DropForeignKey), Is.EqualTo(StepClassification.Destructive));
    }
}

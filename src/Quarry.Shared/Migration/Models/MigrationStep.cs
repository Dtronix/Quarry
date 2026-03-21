namespace Quarry.Shared.Migration;

/// <summary>
/// Represents a single schema change detected by the differ.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
sealed class MigrationStep
{
    public MigrationStepType StepType { get; }
    public StepClassification Classification { get; }
    public string TableName { get; }
    public string? SchemaName { get; }
    public string? OldSchemaName { get; }
    public string? ColumnName { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }
    public string Description { get; }

    public MigrationStep(
        MigrationStepType stepType,
        StepClassification classification,
        string tableName,
        string? schemaName,
        string? columnName,
        object? oldValue,
        object? newValue,
        string description,
        string? oldSchemaName = null)
    {
        StepType = stepType;
        Classification = classification;
        TableName = tableName;
        SchemaName = schemaName;
        OldSchemaName = oldSchemaName;
        ColumnName = columnName;
        OldValue = oldValue;
        NewValue = newValue;
        Description = description;
    }

    /// <summary>
    /// Classifies a step type based on the operation and context.
    /// </summary>
    public static StepClassification Classify(MigrationStepType stepType, ColumnDef? newColumn = null)
    {
        switch (stepType)
        {
            case MigrationStepType.CreateTable:
            case MigrationStepType.AddIndex:
            case MigrationStepType.AddForeignKey:
                return StepClassification.Safe;

            case MigrationStepType.AddColumn:
                if (newColumn != null && !newColumn.IsNullable && !newColumn.HasDefault)
                    return StepClassification.Destructive;
                return StepClassification.Safe;

            case MigrationStepType.AlterColumn:
            case MigrationStepType.RenameTable:
            case MigrationStepType.RenameColumn:
                return StepClassification.Cautious;

            case MigrationStepType.DropTable:
            case MigrationStepType.DropColumn:
            case MigrationStepType.DropIndex:
            case MigrationStepType.DropForeignKey:
                return StepClassification.Destructive;

            default:
                return StepClassification.Cautious;
        }
    }
}

namespace Quarry.Shared.Migration;

/// <summary>
/// The type of schema change represented by a migration step.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
enum MigrationStepType
{
    CreateTable,
    DropTable,
    RenameTable,
    AddColumn,
    DropColumn,
    RenameColumn,
    AlterColumn,
    AddForeignKey,
    DropForeignKey,
    AddIndex,
    DropIndex
}

using System;
using System.Collections.Generic;

namespace Quarry.Shared.Migration;

/// <summary>
/// Severity level for migration notifications.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
enum NotificationLevel
{
    Info,
    Warning
}

/// <summary>
/// A notification about a migration step that may require special handling.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
sealed class MigrationNotification
{
    public NotificationLevel Level { get; }
    public string Message { get; }
    public MigrationStepType StepType { get; }
    public string TableName { get; }

    public MigrationNotification(NotificationLevel level, string message, MigrationStepType stepType, string tableName)
    {
        Level = level;
        Message = message;
        StepType = stepType;
        TableName = tableName;
    }
}

/// <summary>
/// Analyzes migration steps and produces dialect-specific notifications about
/// operations that require table rebuilds, have performance implications, or
/// need manual intervention.
/// <para>
/// To add a new notification, add an entry to <see cref="SQLiteRules"/> (or a
/// future dialect's rule list). Each rule is self-contained — no control-flow
/// changes are needed.
/// </para>
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class MigrationNotificationAnalyzer
{
    // ─── Rule definitions ───────────────────────────────────────────────
    //
    // Each rule maps a step type + condition to a notification.
    //  - StepType:  which MigrationStepType triggers the rule
    //  - Level:     Info or Warning
    //  - Template:  message template ({table} and {column} are replaced)
    //  - Condition: optional predicate for context-dependent rules
    //
    // To add a new notification, just add a new entry to the array.
    // ────────────────────────────────────────────────────────────────────

    private static readonly NotificationRule[] SQLiteRules =
    {
        new(MigrationStepType.AlterColumn, NotificationLevel.Warning,
            "Altering column '{column}' on '{table}' requires a full table rebuild."),

        new(MigrationStepType.DropColumn, NotificationLevel.Warning,
            "Dropping column '{column}' from '{table}' requires a full table rebuild."),

        new(MigrationStepType.AddForeignKey, NotificationLevel.Warning,
            "Adding foreign key to existing table '{table}' requires a full table rebuild.",
            Condition.TableNotCreatedInSameMigration),

        new(MigrationStepType.DropForeignKey, NotificationLevel.Warning,
            "Dropping foreign key from '{table}' requires a full table rebuild."),

        new(MigrationStepType.DropIndex, NotificationLevel.Info,
            "Index on '{table}' will be dropped and recreated.",
            Condition.FollowedByAddIndexOnSameTable),
    };

    // ─── Public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes migration steps for the given dialect and returns notifications.
    /// When dialect is null, SQLite rules are checked with a "SQLite: " prefix
    /// (since it has the most constraints).
    /// </summary>
    public static IReadOnlyList<MigrationNotification> Analyze(
        IReadOnlyList<MigrationStep> steps, string? dialect)
    {
        var notifications = new List<MigrationNotification>();

        if (string.Equals(dialect, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            EvaluateRules(steps, SQLiteRules, null, notifications);
        }
        else if (dialect == null)
        {
            // Unknown dialect — warn using SQLite rules since it's most restrictive
            EvaluateRules(steps, SQLiteRules, "SQLite: ", notifications);
        }

        // Future: add PostgreSQL, MySQL, SqlServer rule arrays here.
        // Example:
        // if (dialect is "postgresql" or null)
        //     EvaluateRules(steps, PostgreSQLRules, prefix, notifications);

        return notifications;
    }

    // ─── Rule engine ────────────────────────────────────────────────────

    private static void EvaluateRules(
        IReadOnlyList<MigrationStep> steps,
        NotificationRule[] rules,
        string? messagePrefix,
        List<MigrationNotification> notifications)
    {
        // Pre-compute context that conditions may need
        var context = new RuleContext(steps);

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            for (var r = 0; r < rules.Length; r++)
            {
                var rule = rules[r];
                if (rule.StepType != step.StepType)
                    continue;

                if (rule.Condition != Condition.None && !EvaluateCondition(rule.Condition, step, i, context))
                    continue;

                var message = (messagePrefix ?? "")
                    + rule.Template
                        .Replace("{table}", step.TableName)
                        .Replace("{column}", step.ColumnName ?? "");

                notifications.Add(new MigrationNotification(rule.Level, message, step.StepType, step.TableName));
            }
        }
    }

    private static bool EvaluateCondition(Condition condition, MigrationStep step, int index, RuleContext context)
    {
        switch (condition)
        {
            case Condition.TableNotCreatedInSameMigration:
                return !context.CreatedTables.Contains(step.TableName);

            case Condition.FollowedByAddIndexOnSameTable:
                for (var j = index + 1; j < context.Steps.Count; j++)
                {
                    if (context.Steps[j].StepType == MigrationStepType.AddIndex
                        && string.Equals(context.Steps[j].TableName, step.TableName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;

            default:
                return true;
        }
    }

    // ─── Supporting types ───────────────────────────────────────────────

    private enum Condition
    {
        None,
        TableNotCreatedInSameMigration,
        FollowedByAddIndexOnSameTable,
    }

    private sealed class NotificationRule
    {
        public MigrationStepType StepType { get; }
        public NotificationLevel Level { get; }
        public string Template { get; }
        public Condition Condition { get; }

        public NotificationRule(MigrationStepType stepType, NotificationLevel level, string template, Condition condition = Condition.None)
        {
            StepType = stepType;
            Level = level;
            Template = template;
            Condition = condition;
        }
    }

    private sealed class RuleContext
    {
        public IReadOnlyList<MigrationStep> Steps { get; }
        public HashSet<string> CreatedTables { get; }

        public RuleContext(IReadOnlyList<MigrationStep> steps)
        {
            Steps = steps;
            CreatedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].StepType == MigrationStepType.CreateTable)
                    CreatedTables.Add(steps[i].TableName);
            }
        }
    }
}

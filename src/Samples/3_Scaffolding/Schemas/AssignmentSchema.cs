// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

public class AssignmentSchema : Schema
{
    public static string Table => "assignments";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> Id => Identity();
    public Ref<CourseSchema, int> CourseId => ForeignKey<CourseSchema, int>(); // ON DELETE CASCADE
    public Col<string> Title { get; }
    public Col<string> DueDate { get; }
    public Col<double> MaxPoints { get; }
    public Col<bool> IsGraded => Default(false);

    // Navigations
    public Many<SubmissionSchema> Submissions => HasMany<SubmissionSchema>(x => x.AssignmentId);

    // Indexes
    public Index IdxAssignmentsCourseDue => Index(CourseId, DueDate);
}

// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

public class SubmissionSchema : Schema
{
    public static string Table => "submissions";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> Id => Identity();
    public Ref<AssignmentSchema, int> AssignmentId => ForeignKey<AssignmentSchema, int>(); // ON DELETE CASCADE
    public Ref<StudentSchema, int> StudentId => ForeignKey<StudentSchema, int>();
    public Col<string> SubmittedAt { get; }
    public Col<double?> Score { get; }
    public Col<byte[]?> FileData { get; }

    // Indexes
    public Index IdxSubmissionsAssignment => Index(AssignmentId);
}

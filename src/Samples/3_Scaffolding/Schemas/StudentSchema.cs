// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

public class StudentSchema : Schema
{
    public static string Table => "students";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> Id => Identity();
    public Col<string> FirstName { get; }
    public Col<string> LastName { get; }
    public Col<string> Email { get; }
    public Col<string> EnrollmentDate { get; }
    public Col<double?> Gpa { get; }
    public Col<byte[]?> Photo { get; }

    // Navigations
    public Many<EnrollmentSchema> Enrollments => HasMany<EnrollmentSchema>(x => x.StudentId);
    public Many<SubmissionSchema> Submissions => HasMany<SubmissionSchema>(x => x.StudentId);

    // Indexes
    public Index IdxStudentsEmail => Index(Email).Unique();
}

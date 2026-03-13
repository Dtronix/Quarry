using Quarry.Shared.Scaffold;

namespace Quarry.Tests.Scaffold;

[TestFixture]
public class JunctionTableDetectorTests
{
    [Test]
    public void Detect_ClassicJunctionTable_DetectsCorrectly()
    {
        var columns = new List<ColumnMetadata>
        {
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "student_id", "course_id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK_sc_student", "student_id", "students", "id"),
            new("FK_sc_course", "course_id", "courses", "id")
        };

        var result = JunctionTableDetector.Detect("student_courses", null, columns, pk, fks, new List<IndexMetadata>());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.LeftFk.ReferencedTable, Is.EqualTo("students"));
        Assert.That(result.RightFk.ReferencedTable, Is.EqualTo("courses"));
        Assert.That(result.ExtraColumns, Is.Empty);
    }

    [Test]
    public void Detect_JunctionWithExtraColumns_DetectsCorrectly()
    {
        var columns = new List<ColumnMetadata>
        {
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false),
            new("enrolled_at", "TEXT", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "student_id", "course_id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK_sc_student", "student_id", "students", "id"),
            new("FK_sc_course", "course_id", "courses", "id")
        };

        var result = JunctionTableDetector.Detect("enrollments", null, columns, pk, fks, new List<IndexMetadata>());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ExtraColumns, Has.Count.EqualTo(1));
        Assert.That(result.ExtraColumns[0].Name, Is.EqualTo("enrolled_at"));
    }

    [Test]
    public void Detect_TooManyExtraColumns_ReturnsNull()
    {
        var columns = new List<ColumnMetadata>
        {
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false),
            new("grade", "TEXT", false),
            new("semester", "TEXT", false),
            new("notes", "TEXT", true)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "student_id", "course_id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "student_id", "students", "id"),
            new("FK2", "course_id", "courses", "id")
        };

        var result = JunctionTableDetector.Detect("enrollments", null, columns, pk, fks, new List<IndexMetadata>());

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Detect_OnlyOneFk_ReturnsNull()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false),
            new("student_id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "student_id", "students", "id")
        };

        var result = JunctionTableDetector.Detect("enrollments", null, columns, pk, fks, new List<IndexMetadata>());

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Detect_MixedCaseColumnNames_MatchesCaseInsensitively()
    {
        // FK columns use different casing from PK columns
        var columns = new List<ColumnMetadata>
        {
            new("Student_Id", "INTEGER", false),
            new("Course_Id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "student_id", "course_id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "Student_Id", "students", "id"),
            new("FK2", "Course_Id", "courses", "id")
        };

        var result = JunctionTableDetector.Detect("student_courses", null, columns, pk, fks, new List<IndexMetadata>());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ExtraColumns, Is.Empty);
    }

    [Test]
    public void Detect_MixedCaseUniqueIndex_MatchesCaseInsensitively()
    {
        // FK columns use different casing from index columns
        var columns = new List<ColumnMetadata>
        {
            new("StudentID", "INTEGER", false),
            new("CourseID", "INTEGER", false)
        };
        PrimaryKeyMetadata? pk = null;
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "StudentID", "students", "id"),
            new("FK2", "CourseID", "courses", "id")
        };
        var indexes = new List<IndexMetadata>
        {
            new("UQ_sc", new[] { "studentid", "courseid" }, isUnique: true)
        };

        var result = JunctionTableDetector.Detect("student_courses", null, columns, pk, fks, indexes);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void Detect_SurrogatePkNotInFks_ExcludedFromExtraColumns()
    {
        // Table has a surrogate PK + 2 FK columns + unique index
        // The surrogate PK column should NOT be counted as an extra column
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "student_id", "students", "id"),
            new("FK2", "course_id", "courses", "id")
        };
        var indexes = new List<IndexMetadata>
        {
            new("UQ_sc", new[] { "student_id", "course_id" }, isUnique: true)
        };

        var result = JunctionTableDetector.Detect("student_courses", null, columns, pk, fks, indexes);

        Assert.That(result, Is.Not.Null);
        // PK column "id" is excluded (it's a PK column), FK columns are excluded → 0 extra
        Assert.That(result!.ExtraColumns, Is.Empty);
    }

    [Test]
    public void Detect_NonPkNonFkColumns_CountedAsExtraColumns()
    {
        // Table has composite PK + 2 FKs + 1 extra payload column
        var columns = new List<ColumnMetadata>
        {
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false),
            new("notes", "TEXT", true)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "student_id", "course_id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "student_id", "students", "id"),
            new("FK2", "course_id", "courses", "id")
        };

        var result = JunctionTableDetector.Detect("student_courses", null, columns, pk, fks, new List<IndexMetadata>());

        Assert.That(result, Is.Not.Null);
        // "notes" is neither PK nor FK → counted as extra
        Assert.That(result!.ExtraColumns, Has.Count.EqualTo(1));
        Assert.That(result.ExtraColumns[0].Name, Is.EqualTo("notes"));
    }

    [Test]
    public void Detect_UniqueIndexInsteadOfCompositePk_DetectsCorrectly()
    {
        var columns = new List<ColumnMetadata>
        {
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false)
        };
        PrimaryKeyMetadata? pk = null; // No composite PK
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "student_id", "students", "id"),
            new("FK2", "course_id", "courses", "id")
        };
        var indexes = new List<IndexMetadata>
        {
            new("UQ_sc", new[] { "student_id", "course_id" }, isUnique: true)
        };

        var result = JunctionTableDetector.Detect("student_courses", null, columns, pk, fks, indexes);

        Assert.That(result, Is.Not.Null);
    }
}

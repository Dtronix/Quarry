using Microsoft.Data.Sqlite;

#if HAS_SCHEMAS
using Quarry;
using Scaffolding;
#endif

// =============================================================================
// Quarry Scaffolding Sample — Database-First Workflow
//
// Demonstrates reverse-engineering an existing SQLite database into Quarry
// schema files, then querying through the generated QuarryContext.
//
// Workflow:
//   1. ./create_and_scaffold.sh  — creates school.db, runs quarry scaffold
//   2. ./query.sh                — queries the database using generated schemas
// =============================================================================

var command = args.Length > 0 ? args[0] : "help";

switch (command)
{
    case "create-db":
        await CreateDatabase();
        break;

    case "query":
#if HAS_SCHEMAS
        await RunQueries();
#else
        Console.Error.WriteLine("Schema files not found.");
        Console.Error.WriteLine("Run ./create_and_scaffold.sh first to create the database and scaffold schemas.");
        return 1;
#endif
        break;

    default:
        Console.WriteLine("Quarry Scaffolding Sample");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- create-db    Create the sample school.db database");
        Console.WriteLine("  dotnet run -- query        Query the database using scaffolded schemas");
        Console.WriteLine();
        Console.WriteLine("Typical workflow:");
        Console.WriteLine("  ./create_and_scaffold.sh   Step 1: create DB + scaffold schemas");
        Console.WriteLine("  ./query.sh                 Step 2: run queries");
        return 0;
}

return 0;

// =============================================================================
// create-db — Pure SQLite, no Quarry dependencies
// =============================================================================

static async Task CreateDatabase()
{
    const string dbPath = "school.db";
    if (File.Exists(dbPath))
    {
        File.Delete(dbPath);
        Console.WriteLine($"Deleted existing {dbPath}");
    }

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();

    // Enable foreign keys (SQLite requires this per-connection)
    using (var pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
    }

    // ── DDL: Tables, FKs, Indexes ──────────────────────────────────────────
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = """
            -- Departments: identity PK, unique index, boolean heuristic column, default
            CREATE TABLE departments (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT    NOT NULL,
                code        TEXT    NOT NULL,
                budget      REAL    NOT NULL DEFAULT 0.0,
                is_active   INTEGER NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX idx_departments_code ON departments(code);

            -- Instructors: FK to departments (CASCADE), composite index, nullable column
            CREATE TABLE instructors (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                first_name      TEXT    NOT NULL,
                last_name       TEXT    NOT NULL,
                email           TEXT    NOT NULL,
                department_id   INTEGER NOT NULL REFERENCES departments(id) ON DELETE CASCADE,
                hire_date       TEXT    NOT NULL,
                bio             TEXT
            );
            CREATE UNIQUE INDEX idx_instructors_email ON instructors(email);
            CREATE INDEX idx_instructors_name ON instructors(last_name, first_name);

            -- Courses: FK to departments, unique index, defaults
            CREATE TABLE courses (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                course_code     TEXT    NOT NULL,
                title           TEXT    NOT NULL,
                credits         INTEGER NOT NULL DEFAULT 3,
                department_id   INTEGER NOT NULL REFERENCES departments(id),
                description     TEXT,
                max_enrollment  INTEGER NOT NULL DEFAULT 30
            );
            CREATE UNIQUE INDEX idx_courses_course_code ON courses(course_code);
            CREATE INDEX idx_courses_department ON courses(department_id);

            -- Students: various types (TEXT, REAL, BLOB), nullable columns
            CREATE TABLE students (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                first_name      TEXT    NOT NULL,
                last_name       TEXT    NOT NULL,
                email           TEXT    NOT NULL,
                enrollment_date TEXT    NOT NULL,
                gpa             REAL,
                photo           BLOB
            );
            CREATE UNIQUE INDEX idx_students_email ON students(email);

            -- Enrollments: junction table (composite PK), FKs with CASCADE
            CREATE TABLE enrollments (
                student_id      INTEGER NOT NULL REFERENCES students(id) ON DELETE CASCADE,
                course_id       INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
                enrolled_date   TEXT    NOT NULL,
                grade           TEXT,
                PRIMARY KEY (student_id, course_id)
            );

            -- Assignments: FK to courses (CASCADE), boolean heuristic, composite index
            CREATE TABLE assignments (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                course_id   INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
                title       TEXT    NOT NULL,
                due_date    TEXT    NOT NULL,
                max_points  REAL    NOT NULL DEFAULT 100.0,
                is_graded   INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX idx_assignments_course_due ON assignments(course_id, due_date);

            -- Submissions: explicit FK to assignments, IMPLICIT FK to students (no constraint!)
            -- The scaffold tool detects student_id as an implicit FK by naming convention.
            CREATE TABLE submissions (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                assignment_id   INTEGER NOT NULL REFERENCES assignments(id) ON DELETE CASCADE,
                student_id      INTEGER NOT NULL,
                submitted_at    TEXT    NOT NULL,
                score           REAL,
                file_data       BLOB
            );
            CREATE INDEX idx_submissions_assignment ON submissions(assignment_id);

            -- Course prerequisites: self-referencing junction table (composite PK)
            CREATE TABLE course_prerequisites (
                course_id       INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
                prerequisite_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
                PRIMARY KEY (course_id, prerequisite_id)
            );
            """;

        await cmd.ExecuteNonQueryAsync();
    }

    Console.WriteLine("Created 8 tables with indexes and foreign keys.");

    // ── Seed Data ──────────────────────────────────────────────────────────
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = """
            -- Departments
            INSERT INTO departments (name, code, budget, is_active) VALUES
                ('Computer Science', 'CS',   500000.0, 1),
                ('Mathematics',      'MATH', 300000.0, 1),
                ('Physics',          'PHYS', 350000.0, 1);

            -- Instructors
            INSERT INTO instructors (first_name, last_name, email, department_id, hire_date, bio) VALUES
                ('Alan',    'Turing',  'turing@school.edu',  1, '2020-01-15', 'Pioneer in computation theory'),
                ('Grace',   'Hopper',  'hopper@school.edu',  1, '2019-09-01', NULL),
                ('Carl',    'Gauss',   'gauss@school.edu',   2, '2021-03-10', 'Mathematician and physicist'),
                ('Emmy',    'Noether', 'noether@school.edu', 2, '2020-08-20', NULL),
                ('Richard', 'Feynman', 'feynman@school.edu', 3, '2018-11-01', 'Quantum physics researcher');

            -- Courses
            INSERT INTO courses (course_code, title, credits, department_id, description, max_enrollment) VALUES
                ('CS101',   'Intro to Programming', 3, 1, 'Learn the basics of programming',   40),
                ('CS201',   'Data Structures',      3, 1, 'Advanced data structures',           30),
                ('CS301',   'Algorithms',           4, 1, NULL,                                 25),
                ('MATH101', 'Calculus I',           4, 2, 'Limits, derivatives, integrals',     35),
                ('MATH201', 'Linear Algebra',       3, 2, NULL,                                 30),
                ('PHYS101', 'Physics I',            4, 3, 'Mechanics and thermodynamics',       35),
                ('PHYS201', 'Quantum Mechanics',    3, 3, NULL,                                 20),
                ('CS401',   'Machine Learning',     3, 1, 'Neural networks and deep learning',  20);

            -- Course prerequisites
            INSERT INTO course_prerequisites (course_id, prerequisite_id) VALUES
                (2, 1),  -- CS201 requires CS101
                (3, 2),  -- CS301 requires CS201
                (8, 3),  -- CS401 requires CS301
                (8, 5),  -- CS401 requires MATH201
                (5, 4),  -- MATH201 requires MATH101
                (7, 6),  -- PHYS201 requires PHYS101
                (7, 4);  -- PHYS201 requires MATH101

            -- Students
            INSERT INTO students (first_name, last_name, email, enrollment_date, gpa, photo) VALUES
                ('Alice',  'Johnson',  'alice@students.edu',  '2023-09-01', 3.8,  NULL),
                ('Bob',    'Smith',    'bob@students.edu',    '2023-09-01', 3.2,  NULL),
                ('Carol',  'Williams', 'carol@students.edu',  '2022-09-01', 3.9,  NULL),
                ('Dave',   'Brown',    'dave@students.edu',   '2024-01-15', 2.7,  NULL),
                ('Eve',    'Davis',    'eve@students.edu',    '2023-01-15', 3.5,  NULL),
                ('Frank',  'Miller',   'frank@students.edu',  '2024-09-01', NULL, NULL);

            -- Enrollments (junction: student_id + course_id)
            INSERT INTO enrollments (student_id, course_id, enrolled_date, grade) VALUES
                (1, 1, '2023-09-01', 'A'),   -- Alice: CS101
                (1, 2, '2024-01-15', 'A-'),  -- Alice: CS201
                (1, 4, '2023-09-01', 'B+'),  -- Alice: MATH101
                (1, 5, '2024-01-15', NULL),  -- Alice: MATH201 (in progress)
                (2, 1, '2023-09-01', 'B'),   -- Bob: CS101
                (2, 6, '2024-01-15', 'B+'),  -- Bob: PHYS101
                (3, 2, '2023-01-15', 'A'),   -- Carol: CS201
                (3, 3, '2023-09-01', NULL),  -- Carol: CS301 (in progress)
                (3, 5, '2023-01-15', 'A-'),  -- Carol: MATH201
                (4, 1, '2024-01-15', 'C+'),  -- Dave: CS101
                (4, 4, '2024-01-15', 'B-'),  -- Dave: MATH101
                (5, 1, '2023-01-15', 'A-'),  -- Eve: CS101
                (5, 2, '2023-09-01', 'B+'),  -- Eve: CS201
                (5, 6, '2024-01-15', NULL),  -- Eve: PHYS101 (in progress)
                (6, 1, '2024-09-01', NULL);  -- Frank: CS101 (in progress)

            -- Assignments
            INSERT INTO assignments (course_id, title, due_date, max_points, is_graded) VALUES
                (1, 'Hello World',               '2024-02-01', 50.0,  1),
                (2, 'Linked List Implementation', '2024-03-01', 100.0, 1),
                (3, 'Sorting Analysis',           '2024-04-01', 100.0, 0),
                (4, 'Derivative Worksheet',       '2024-02-15', 80.0,  1),
                (5, 'Matrix Operations',          '2024-03-15', 100.0, 1),
                (6, 'Projectile Motion Lab',      '2024-03-01', 75.0,  1);

            -- Submissions (student_id has NO FK constraint — implicit FK)
            INSERT INTO submissions (assignment_id, student_id, submitted_at, score, file_data) VALUES
                (1, 1, '2024-01-30 14:00:00', 48.0, NULL),  -- Alice: Hello World
                (1, 2, '2024-02-01 09:30:00', 35.0, NULL),  -- Bob: Hello World
                (1, 4, '2024-02-01 23:59:00', 28.0, NULL),  -- Dave: Hello World
                (1, 5, '2024-01-29 10:00:00', 47.0, NULL),  -- Eve: Hello World
                (2, 1, '2024-02-28 16:00:00', 92.0, NULL),  -- Alice: Linked List
                (2, 3, '2024-02-27 11:30:00', 98.0, NULL),  -- Carol: Linked List
                (2, 5, '2024-03-01 08:00:00', 78.0, NULL),  -- Eve: Linked List
                (4, 1, '2024-02-14 12:00:00', 72.0, NULL),  -- Alice: Derivatives
                (4, 4, '2024-02-15 20:00:00', 55.0, NULL),  -- Dave: Derivatives
                (5, 1, '2024-03-14 15:00:00', 88.0, NULL),  -- Alice: Matrix Ops
                (5, 3, '2024-03-13 09:00:00', 95.0, NULL),  -- Carol: Matrix Ops
                (6, 2, '2024-02-28 17:00:00', 68.0, NULL),  -- Bob: Projectile Motion
                (6, 5, '2024-03-01 12:00:00', NULL, NULL);  -- Eve: Projectile Motion (ungraded)
            """;

        await cmd.ExecuteNonQueryAsync();
    }

    Console.WriteLine("Inserted seed data: 3 departments, 5 instructors, 8 courses,");
    Console.WriteLine("  7 prerequisites, 6 students, 15 enrollments, 6 assignments, 13 submissions.");
    Console.WriteLine();
    Console.WriteLine($"Database created: {Path.GetFullPath(dbPath)}");
}

// =============================================================================
// query — Uses the scaffolded SchoolDbContext and Quarry's QueryBuilder API
// =============================================================================

#if HAS_SCHEMAS

static async Task RunQueries()
{
    using var connection = new SqliteConnection("Data Source=school.db");
    await connection.OpenAsync();

    var db = new SchoolDbContext(connection);

    Console.WriteLine("Quarry Scaffolding Sample — Query Demo");
    Console.WriteLine(new string('=', 50));

    // ── 1. Select all departments ──────────────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("1. All Departments");
    Console.WriteLine(new string('-', 40));

    var departments = await db.Departments
        .Select(d => (d.Id, d.Name, d.Code, d.Budget, d.IsActive))
        .ExecuteFetchAllAsync();

    foreach (var d in departments)
        Console.WriteLine($"  [{d.Id}] {d.Name} ({d.Code}) — Budget: ${d.Budget:N0}, Active: {d.IsActive}");

    // ── 2. Honor roll students (GPA >= 3.5) ────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("2. Honor Roll Students (GPA >= 3.5)");
    Console.WriteLine(new string('-', 40));

    var honorRoll = await db.Students
        .Where(s => s.Gpa >= 3.5)
        .Select(s => (s.FirstName, s.LastName, s.Gpa))
        .OrderBy(s => s.Gpa, Direction.Descending)
        .ExecuteFetchAllAsync();

    foreach (var s in honorRoll)
        Console.WriteLine($"  {s.FirstName} {s.LastName} — GPA: {s.Gpa:F1}");

    // ── 3. Top 3 students by GPA ──────────────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("3. Top 3 Students by GPA");
    Console.WriteLine(new string('-', 40));

    var topStudents = await db.Students
        .Where(s => s.Gpa != null)
        .Select(s => (s.FirstName, s.LastName, s.Gpa, s.EnrollmentDate))
        .OrderBy(s => s.Gpa, Direction.Descending)
        .Limit(3)
        .ExecuteFetchAllAsync();

    for (var i = 0; i < topStudents.Count; i++)
    {
        var s = topStudents[i];
        Console.WriteLine($"  #{i + 1}: {s.FirstName} {s.LastName} — GPA: {s.Gpa:F1}, Enrolled: {s.EnrollmentDate}");
    }

    // ── 4. Courses with department (join via FK) ──────────────────────────
    Console.WriteLine();
    Console.WriteLine("4. Courses with Department (Join)");
    Console.WriteLine(new string('-', 40));

    var coursesWithDept = await db.Courses
        .Join<Department>((c, d) => c.DepartmentId.Id == d.Id)
        .Select((c, d) => (c.CourseCode, c.Title, c.Credits, Department: d.Name))
        .OrderBy((c, d) => c.CourseCode)
        .ExecuteFetchAllAsync();

    foreach (var c in coursesWithDept)
        Console.WriteLine($"  {c.CourseCode}: {c.Title} ({c.Credits} cr) — {c.Department}");

    // ── 5. Instructors with department (join via FK) ──────────────────────
    Console.WriteLine();
    Console.WriteLine("5. Instructors with Department (Join)");
    Console.WriteLine(new string('-', 40));

    var instructors = await db.Instructors
        .Join<Department>((i, d) => i.DepartmentId.Id == d.Id)
        .Select((i, d) => (i.FirstName, i.LastName, Department: d.Name, i.HireDate))
        .OrderBy((i, d) => i.LastName)
        .ExecuteFetchAllAsync();

    foreach (var i in instructors)
        Console.WriteLine($"  {i.FirstName} {i.LastName} — {i.Department}, since {i.HireDate}");

    // ── 6. CRUD: Insert, Read, Update, Delete ─────────────────────────────
    Console.WriteLine();
    Console.WriteLine("6. CRUD Demo (Insert → Read → Update → Read → Delete)");
    Console.WriteLine(new string('-', 40));

    // Insert
    var newId = await db.Insert(new Student
    {
        FirstName = "Grace",
        LastName = "Murray",
        Email = "grace@students.edu",
        EnrollmentDate = "2025-09-01"
    }).ExecuteScalarAsync<int>();
    Console.WriteLine($"  INSERT: Created student #{newId} (Grace Murray)");

    // Read back
    var inserted = await db.Students
        .Where(s => s.Id == newId)
        .Select(s => (s.Id, s.FirstName, s.LastName, s.Gpa))
        .ExecuteFetchFirstAsync();
    Console.WriteLine($"  READ:   [{inserted.Id}] {inserted.FirstName} {inserted.LastName}, GPA: {(inserted.Gpa.HasValue ? inserted.Gpa.Value.ToString("F1") : "(null)")}");

    // Update
    await db.Update<Student>()
        .Set(s => s.Gpa, 3.7)
        .Where(s => s.Id == newId)
        .ExecuteNonQueryAsync();
    Console.WriteLine($"  UPDATE: Set GPA to 3.7");

    // Read updated
    var updated = await db.Students
        .Where(s => s.Id == newId)
        .Select(s => (s.Id, s.FirstName, s.LastName, s.Gpa))
        .ExecuteFetchFirstAsync();
    Console.WriteLine($"  READ:   [{updated.Id}] {updated.FirstName} {updated.LastName}, GPA: {updated.Gpa:F1}");

    // Delete
    var deleted = await db.Delete<Student>()
        .Where(s => s.Id == newId)
        .ExecuteNonQueryAsync();
    Console.WriteLine($"  DELETE: Removed {deleted} row(s)");

    // Verify count
    var count = await db.Students
        .Select(s => s.Id)
        .ExecuteFetchAllAsync();
    Console.WriteLine($"  VERIFY: {count.Count} students remain (back to original)");

    Console.WriteLine();
    Console.WriteLine("Done.");
}

#endif

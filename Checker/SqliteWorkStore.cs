using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;

namespace Checker
{
    /// <summary>
    ///     Реализация IWorkStore через SQLite + Dapper.
    ///     Файлик БД создаётся рядом с приложением (checker.db).
    /// </summary>
    public sealed class SqliteWorkStore : IWorkStore
    {
        private readonly string _connectionString;

        public SqliteWorkStore(string? connectionString = null)
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                _connectionString = connectionString;
            }
            else
            {
                var dbPath = Path.Combine(AppContext.BaseDirectory, "checker.db");
                _connectionString = $"Data Source={dbPath}";
            }

            using var connection = CreateConnection();
            connection.Open();
            EnsureSchema(connection);
        }

        // ---------------- IWorkStore implementation ----------------

        public WorkEntity AddWork(WorkEntity work)
        {
            using var connection = CreateConnection();
            connection.Open();

            connection.Execute(@"
INSERT INTO Works (WorkId, StudentId, StudentName, AssignmentId, FileId, FileHash, CreatedAt)
VALUES (@WorkId, @StudentId, @StudentName, @AssignmentId, @FileId, @FileHash, @CreatedAt);",
                new
                {
                    WorkId = work.WorkId.ToString(),
                    work.StudentId,
                    work.StudentName,
                    work.AssignmentId,
                    work.FileId,
                    work.FileHash,
                    CreatedAt = work.CreatedAt.ToString("O") // ISO-8601
                });

            return work;
        }

        public ReportEntity AddReport(ReportEntity report)
        {
            using var connection = CreateConnection();
            connection.Open();

            connection.Execute(@"
INSERT INTO Reports (ReportId, WorkId, IsPlagiarism, SourceWorkId, PlagiarismScore, CreatedAt)
VALUES (@ReportId, @WorkId, @IsPlagiarism, @SourceWorkId, @PlagiarismScore, @CreatedAt);",
                new
                {
                    ReportId = report.ReportId.ToString(),
                    WorkId = report.WorkId.ToString(),
                    IsPlagiarism = report.IsPlagiarism ? 1 : 0,
                    SourceWorkId = report.SourceWorkId.HasValue ? report.SourceWorkId.Value.ToString() : null,
                    report.PlagiarismScore,
                    CreatedAt = report.CreatedAt.ToString("O")
                });

            return report;
        }

        public IEnumerable<WorkEntity> GetAllWorks()
        {
            using var connection = CreateConnection();
            connection.Open();

            var rows = connection.Query<WorkRow>(@"
SELECT WorkId, StudentId, StudentName, AssignmentId, FileId, FileHash, CreatedAt
FROM Works;");

            return rows.Select(MapWork).ToList();
        }

        public IEnumerable<ReportEntity> GetReportsForWork(Guid workId)
        {
            using var connection = CreateConnection();
            connection.Open();

            var rows = connection.Query<ReportRow>(@"
SELECT ReportId, WorkId, IsPlagiarism, SourceWorkId, PlagiarismScore, CreatedAt
FROM Reports
WHERE WorkId = @WorkId;",
                new { WorkId = workId.ToString() });

            return rows.Select(MapReport).ToList();
        }

        public IEnumerable<(WorkEntity Work, ReportEntity Report)> GetByAssignment(string assignmentId)
        {
            using var connection = CreateConnection();
            connection.Open();

            var workRows = connection.Query<WorkRow>(@"
SELECT WorkId, StudentId, StudentName, AssignmentId, FileId, FileHash, CreatedAt
FROM Works
WHERE AssignmentId = @AssignmentId;",
                new { AssignmentId = assignmentId }).ToList();

            var result = new List<(WorkEntity, ReportEntity)>();

            foreach (var wRow in workRows)
            {
                var work = MapWork(wRow);

                var reportRows = connection.Query<ReportRow>(@"
SELECT ReportId, WorkId, IsPlagiarism, SourceWorkId, PlagiarismScore, CreatedAt
FROM Reports
WHERE WorkId = @WorkId;",
                    new { wRow.WorkId });

                foreach (var rRow in reportRows)
                {
                    var report = MapReport(rRow);
                    result.Add((work, report));
                }
            }

            return result;
        }

        private IDbConnection CreateConnection()
        {
            return new SqliteConnection(_connectionString);
        }

        private static void EnsureSchema(IDbConnection connection)
        {
            // Таблица работ
            connection.Execute(@"
CREATE TABLE IF NOT EXISTS Works (
    WorkId TEXT PRIMARY KEY,
    StudentId TEXT NOT NULL,
    StudentName TEXT NOT NULL,
    AssignmentId TEXT NOT NULL,
    FileId TEXT NOT NULL,
    FileHash TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");

            // Таблица отчётов
            connection.Execute(@"
CREATE TABLE IF NOT EXISTS Reports (
    ReportId TEXT PRIMARY KEY,
    WorkId TEXT NOT NULL,
    IsPlagiarism INTEGER NOT NULL,
    SourceWorkId TEXT NULL,
    PlagiarismScore INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL
);");
        }

        // ---------------- Маппинг в доменные сущности ----------------

        private static WorkEntity MapWork(WorkRow row)
        {
            return new WorkEntity(
                Guid.Parse(row.WorkId),
                row.StudentId,
                row.StudentName,
                row.AssignmentId,
                row.FileId,
                row.FileHash,
                DateTime.Parse(row.CreatedAt, null, DateTimeStyles.RoundtripKind));
        }

        private static ReportEntity MapReport(ReportRow row)
        {
            return new ReportEntity(
                Guid.Parse(row.ReportId),
                Guid.Parse(row.WorkId),
                row.IsPlagiarism != 0,
                string.IsNullOrWhiteSpace(row.SourceWorkId)
                    ? null
                    : Guid.Parse(row.SourceWorkId),
                row.PlagiarismScore,
                DateTime.Parse(row.CreatedAt, null, DateTimeStyles.RoundtripKind));
        }

        // ---------------- DTO для чтения из SQLite ----------------

        public class WorkRow
        {
            public string WorkId { get; set; } = default!;
            public string StudentId { get; set; } = default!;
            public string StudentName { get; set; } = default!;
            public string AssignmentId { get; set; } = default!;
            public string FileId { get; set; } = default!;
            public string FileHash { get; set; } = default!;
            public string CreatedAt { get; set; } = default!;
        }

        public class ReportRow
        {
            public string ReportId { get; set; } = default!;
            public string WorkId { get; set; } = default!;
            public long IsPlagiarism { get; set; }
            public string? SourceWorkId { get; set; }
            public int PlagiarismScore { get; set; }
            public string CreatedAt { get; set; } = default!;
        }
    }
}
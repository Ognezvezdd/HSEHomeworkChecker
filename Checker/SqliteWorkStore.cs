using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Checker
{
    /// <summary>
    /// Реализация IWorkStore через SQLite + Dapper.
    /// Файлик БД создаётся рядом с приложением (checker.db).
    /// </summary>
    public sealed class SqliteWorkStore : IWorkStore
    {
        private readonly string _connectionString;

        public SqliteWorkStore(string connectionString)
        {
            _connectionString = connectionString;
            EnsureDatabase();
        }

        private IDbConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        /// <summary>
        /// Создаём таблицы, если их ещё нет.
        /// </summary>
        private void EnsureDatabase()
        {
            using var conn = OpenConnection();

            const string createWorksTable = """
                                            CREATE TABLE IF NOT EXISTS Works (
                                                WorkId        TEXT PRIMARY KEY,
                                                StudentId     TEXT NOT NULL,
                                                StudentName   TEXT NOT NULL,
                                                AssignmentId  TEXT NOT NULL,
                                                FileId        TEXT NOT NULL,
                                                FileHash      TEXT NOT NULL,
                                                CreatedAt     TEXT NOT NULL
                                            );
                                            """;

            const string createReportsTable = """
                                              CREATE TABLE IF NOT EXISTS Reports (
                                                  ReportId         TEXT PRIMARY KEY,
                                                  WorkId           TEXT NOT NULL,
                                                  IsPlagiarism     INTEGER NOT NULL,
                                                  SourceWorkId     TEXT NULL,
                                                  PlagiarismScore  INTEGER NOT NULL,
                                                  CreatedAt        TEXT NOT NULL,
                                                  FOREIGN KEY (WorkId) REFERENCES Works (WorkId)
                                              );
                                              """;

            const string createIndexAssignment = """
                                                 CREATE INDEX IF NOT EXISTS IX_Works_AssignmentId
                                                     ON Works (AssignmentId);
                                                 """;

            const string createIndexWorkIdReports = """
                                                    CREATE INDEX IF NOT EXISTS IX_Reports_WorkId
                                                        ON Reports (WorkId);
                                                    """;

            conn.Execute(createWorksTable);
            conn.Execute(createReportsTable);
            conn.Execute(createIndexAssignment);
            conn.Execute(createIndexWorkIdReports);
        }

        public WorkEntity AddWork(WorkEntity work)
        {
            const string sql = """
                               INSERT INTO Works (
                                   WorkId, StudentId, StudentName, AssignmentId, FileId, FileHash, CreatedAt
                               )
                               VALUES (
                                   @WorkId, @StudentId, @StudentName, @AssignmentId, @FileId, @FileHash, @CreatedAt
                               );
                               """;

            using var conn = OpenConnection();
            conn.Execute(sql, work);
            return work;
        }

        public ReportEntity AddReport(ReportEntity report)
        {
            const string sql = """
                               INSERT INTO Reports (
                                   ReportId, WorkId, IsPlagiarism, SourceWorkId, PlagiarismScore, CreatedAt
                               )
                               VALUES (
                                   @ReportId, @WorkId, @IsPlagiarism, @SourceWorkId, @PlagiarismScore, @CreatedAt
                               );
                               """;

            using var conn = OpenConnection();
            conn.Execute(sql, new
            {
                report.ReportId,
                report.WorkId,
                // bool → int (0/1)
                IsPlagiarism = report.IsPlagiarism ? 1 : 0,
                report.SourceWorkId,
                report.PlagiarismScore,
                report.CreatedAt
            });

            return report;
        }

        public IEnumerable<WorkEntity> GetAllWorks()
        {
            const string sql =
                "SELECT WorkId, StudentId, StudentName, AssignmentId, FileId, FileHash, CreatedAt FROM Works;";
            using var conn = OpenConnection();
            return conn.Query<WorkEntity>(sql).ToList();
        }

        public IEnumerable<ReportEntity> GetReportsForWork(Guid workId)
        {
            const string sql = """
                               SELECT
                                   ReportId,
                                   WorkId,
                                   IsPlagiarism,
                                   SourceWorkId,
                                   PlagiarismScore,
                                   CreatedAt
                               FROM Reports
                               WHERE WorkId = @WorkId;
                               """;

            using var conn = OpenConnection();
            // Dapper сам преобразует 0/1 в bool
            return conn.Query<ReportEntity>(sql, new { WorkId = workId }).ToList();
        }

        public IEnumerable<(WorkEntity Work, ReportEntity Report)> GetByAssignment(string assignmentId)
        {
            using var conn = OpenConnection();

            const string sqlWorks = """
                                    SELECT WorkId, StudentId, StudentName, AssignmentId, FileId, FileHash, CreatedAt
                                    FROM Works
                                    WHERE AssignmentId = @AssignmentId;
                                    """;

            var works = conn.Query<WorkEntity>(sqlWorks, new { AssignmentId = assignmentId }).ToList();
            if (works.Count == 0)
            {
                yield break;
            }

            const string sqlReports = """
                                      SELECT
                                          ReportId,
                                          WorkId,
                                          IsPlagiarism,
                                          SourceWorkId,
                                          PlagiarismScore,
                                          CreatedAt
                                      FROM Reports
                                      WHERE WorkId = @WorkId;
                                      """;

            foreach (var work in works)
            {
                var reports = conn.Query<ReportEntity>(sqlReports, new { work.WorkId });
                foreach (var report in reports)
                {
                    yield return (work, report);
                }
            }
        }
    }
}
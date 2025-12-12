using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Checker
{
    /// <summary>Запрос на создание проверки работы студента.</summary>
    public record CreateWorkRequest(
        string StudentId,
        string StudentName,
        string AssignmentId,
        string FileId);

    /// <summary>Результат создания работы и отчёта по проверке.</summary>
    public record CreateWorkResponse(
        Guid WorkId,
        Guid ReportId,
        bool IsPlagiarism);

    public record WorkReportDto(
        Guid ReportId,
        Guid WorkId,
        string StudentId,
        string StudentName,
        string AssignmentId,
        bool IsPlagiarism,
        Guid? SourceWorkId,
        int PlagiarismScore,
        DateTime CreatedAt);

    public record AssignmentSummaryDto(
        string AssignmentId,
        int TotalWorks,
        int PlagiarisedCount);

    public sealed class WorkEntity
    {
        public Guid WorkId { get; set; }
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string AssignmentId { get; set; } = null!;
        public string FileId { get; set; } = null!;
        public string FileHash { get; set; } = null!;
        public DateTime CreatedAt { get; set; }

        // Нужен Dapper'у
        public WorkEntity()
        {
        }

        // Удобный конструктор для твоего кода
        public WorkEntity(
            Guid workId,
            string studentId,
            string studentName,
            string assignmentId,
            string fileId,
            string fileHash,
            DateTime createdAt)
        {
            WorkId = workId;
            StudentId = studentId;
            StudentName = studentName;
            AssignmentId = assignmentId;
            FileId = fileId;
            FileHash = fileHash;
            CreatedAt = createdAt;
        }
    }

    public sealed class ReportEntity
    {
        public Guid ReportId { get; set; }
        public Guid WorkId { get; set; }
        public bool IsPlagiarism { get; set; }
        public Guid? SourceWorkId { get; set; }
        public int PlagiarismScore { get; set; }
        public DateTime CreatedAt { get; set; }

        public ReportEntity()
        {
        }

        public ReportEntity(
            Guid reportId,
            Guid workId,
            bool isPlagiarism,
            Guid? sourceWorkId,
            int plagiarismScore,
            DateTime createdAt)
        {
            ReportId = reportId;
            WorkId = workId;
            IsPlagiarism = isPlagiarism;
            SourceWorkId = sourceWorkId;
            PlagiarismScore = plagiarismScore;
            CreatedAt = createdAt;
        }
    }

    /// <summary>Контракт хранилища работ и отчётов по проверке.</summary>
    public interface IWorkStore
    {
        WorkEntity AddWork(WorkEntity work);
        ReportEntity AddReport(ReportEntity report);
        IEnumerable<WorkEntity> GetAllWorks();
        IEnumerable<ReportEntity> GetReportsForWork(Guid workId);
        IEnumerable<(WorkEntity Work, ReportEntity Report)> GetByAssignment(string assignmentId);
    }

    /// <summary>In-memory хранилище работ и отчётов (Заменено на СУБД, оставлено для тестов и безопасности)</summary>
    public sealed class InMemoryWorkStore : IWorkStore
    {
        private readonly ConcurrentDictionary<Guid, ReportEntity> _reports = new();
        private readonly ConcurrentDictionary<Guid, WorkEntity> _works = new();

        public WorkEntity AddWork(WorkEntity work)
        {
            _works[work.WorkId] = work;
            return work;
        }

        public ReportEntity AddReport(ReportEntity report)
        {
            _reports[report.ReportId] = report;
            return report;
        }

        public IEnumerable<WorkEntity> GetAllWorks()
        {
            return _works.Values;
        }

        public IEnumerable<ReportEntity> GetReportsForWork(Guid workId)
        {
            return _reports.Values.Where(r => r.WorkId == workId);
        }

        public IEnumerable<(WorkEntity Work, ReportEntity Report)> GetByAssignment(string assignmentId)
        {
            var worksByAssignment = _works.Values.Where(w =>
                w.AssignmentId.Equals(assignmentId, StringComparison.OrdinalIgnoreCase));

            foreach (var w in worksByAssignment)
            {
                var reports = _reports.Values.Where(r => r.WorkId == w.WorkId);
                foreach (var r in reports)
                {
                    yield return (w, r);
                }
            }
        }
    }

    public interface IFileStorageClient
    {
        Task<byte[]> GetFileBytesAsync(string fileId, CancellationToken ct = default);
    }

    public sealed class FileStorageClient(HttpClient http) : IFileStorageClient
    {
        public async Task<byte[]> GetFileBytesAsync(string fileId, CancellationToken ct = default)
        {
            var response = await http.GetAsync($"/internal/files/{fileId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"FileStorage returned {(int)response.StatusCode}");
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }
    }

    /// <summary>Контракт сервиса проверки работ на плагиат.</summary>
    public interface IPlagiarismDetector
    {
        Task<(bool isPlagiarism, WorkEntity? sourceWork, int score)> AnalyzeAsync(
            string assignmentId,
            string studentId,
            string fileId,
            IFileStorageClient storageClient,
            IWorkStore store,
            CancellationToken ct = default);
    }

    public sealed class PlagiarismDetector : IPlagiarismDetector
    {
        /// <summary>Анализирует работу на плагиат в рамках задания и ищет исходную работу в хранилище.</summary>
        public async Task<(bool isPlagiarism, WorkEntity? sourceWork, int score)> AnalyzeAsync(
            string assignmentId,
            string studentId,
            string fileId,
            IFileStorageClient storageClient,
            IWorkStore store,
            CancellationToken ct = default)
        {
            var bytes = await storageClient.GetFileBytesAsync(fileId, ct);
            var hash = ComputeHash(bytes);

            var existing = store.GetAllWorks()
                .Where(w =>
                    w.AssignmentId.Equals(assignmentId, StringComparison.OrdinalIgnoreCase) &&
                    w.FileHash == hash &&
                    !w.StudentId.Equals(studentId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(w => w.CreatedAt)
                .FirstOrDefault();

            if (existing is null)
            {
                return (false, null, 0);
            }

            return (true, existing, 100);
        }

        private static string ComputeHash(byte[] bytes)
        {
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
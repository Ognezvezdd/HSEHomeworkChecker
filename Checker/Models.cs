using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Checker
{
    public record CreateWorkRequest(
        string StudentId,
        string StudentName,
        string AssignmentId,
        string FileId);

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

    public record WorkEntity(
        Guid WorkId,
        string StudentId,
        string StudentName,
        string AssignmentId,
        string FileId,
        string FileHash,
        DateTime CreatedAt);

    public record ReportEntity(
        Guid ReportId,
        Guid WorkId,
        bool IsPlagiarism,
        Guid? SourceWorkId,
        int PlagiarismScore,
        DateTime CreatedAt);


// === Хранилище работ и отчётов ===

    public interface IWorkStore
    {
        WorkEntity AddWork(WorkEntity work);
        ReportEntity AddReport(ReportEntity report);
        IEnumerable<WorkEntity> GetAllWorks();
        IEnumerable<ReportEntity> GetReportsForWork(Guid workId);
        IEnumerable<(WorkEntity Work, ReportEntity Report)> GetByAssignment(string assignmentId);
    }

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


// === Клиент FileStorage ===

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


// === Антиплагиат ===

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
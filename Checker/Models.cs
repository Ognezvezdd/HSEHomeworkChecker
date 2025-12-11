namespace Checker
{
    public record CreateWorkRequest(
        string StudentId,
        string StudentName,
        string AssignmentId,
        Guid FileId);

    public record CreateWorkResponse(
        Guid WorkId,
        Guid ReportId,
        string Status,
        DateTimeOffset SubmittedAt);

    public record Work(
        Guid Id,
        string StudentId,
        string StudentName,
        string AssignmentId,
        Guid FileId,
        DateTimeOffset SubmittedAt);

    public record Report(
        Guid Id,
        Guid WorkId,
        bool IsPlagiarism,
        double PlagiarismScore,
        Guid? SourceWorkId,
        DateTimeOffset CreatedAt);

    public record AssignmentSummary(
        string AssignmentId,
        int TotalWorks,
        int PlagiarismCount);

    public sealed class InMemoryWorkStore
    {
        private readonly List<Report> _reports = new();
        private readonly List<Work> _works = new();

        public void AddWork(Work work)
        {
            _works.Add(work);
        }

        public void AddReport(Report report)
        {
            _reports.Add(report);
        }

        public IEnumerable<Report> GetReportsByWork(Guid workId)
        {
            return _reports.Where(r => r.WorkId == workId);
        }

        public AssignmentSummary GetAssignmentSummary(string assignmentId)
        {
            var works = _works.Where(w => w.AssignmentId == assignmentId).ToList();
            if (!works.Any())
            {
                return new AssignmentSummary(assignmentId, 0, 0);
            }

            var workIds = works.Select(w => w.Id).ToHashSet();
            var reports = _reports.Where(r => workIds.Contains(r.WorkId)).ToList();
            var plagiarismCount = reports.Count(r => r.IsPlagiarism);

            return new AssignmentSummary(assignmentId, works.Count, plagiarismCount);
        }
    }
}
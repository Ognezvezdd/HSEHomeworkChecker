namespace PublicApi
{
    public record PublicWorkCreatedResponse(
        Guid WorkId,
        Guid ReportId,
        string Status,
        DateTimeOffset SubmittedAt);

    public record PublicReportDto(
        Guid Id,
        Guid WorkId,
        bool IsPlagiarism,
        double PlagiarismScore,
        Guid? SourceWorkId,
        DateTimeOffset CreatedAt);

    public record PublicAssignmentSummaryDto(
        string AssignmentId,
        int TotalWorks,
        int PlagiarismCount);
}
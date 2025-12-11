using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PublicApi
{
// === DTO для Public API ===

    public record PublicCreateWorkResponse(
        Guid WorkId,
        Guid ReportId,
        bool IsPlagiarism,
        string FileId);

    public record PublicWorkReportDto(
        Guid ReportId,
        Guid WorkId,
        string StudentId,
        string StudentName,
        string AssignmentId,
        bool IsPlagiarism,
        Guid? SourceWorkId,
        int PlagiarismScore,
        DateTime CreatedAt);

    public record PublicAssignmentSummaryDto(
        string AssignmentId,
        int TotalWorks,
        int PlagiarisedCount);


// === Контракты внутренних сервисов (Checker) ===

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


// === Клиенты внутренних сервисов ===

    public interface IFileStorageApiClient
    {
        Task<string> UploadAsync(IFormFile file, CancellationToken ct = default);
    }

    public sealed class FileStorageApiClient : IFileStorageApiClient
    {
        private readonly HttpClient _http;

        public FileStorageApiClient(HttpClient http)
        {
            _http = http;
        }

        public async Task<string> UploadAsync(IFormFile file, CancellationToken ct = default)
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(file.OpenReadStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "file", file.FileName);

            var response = await _http.PostAsync("/internal/files", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"FileStorage returned {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("fileId", out var idProp))
            {
                return idProp.GetString() ?? throw new InvalidOperationException("fileId is null");
            }

            throw new InvalidOperationException("fileId not found in response");
        }
    }

    public interface ICheckerApiClient
    {
        Task<CreateWorkResponse> CreateWorkAsync(CreateWorkRequest request, CancellationToken ct = default);
        Task<List<WorkReportDto>> GetReportsForWorkAsync(Guid workId, CancellationToken ct = default);
        Task<AssignmentSummaryDto?> GetAssignmentSummaryAsync(string assignmentId, CancellationToken ct = default);
    }

    public sealed class CheckerApiClient : ICheckerApiClient
    {
        private readonly HttpClient _http;

        public CheckerApiClient(HttpClient http)
        {
            _http = http;
        }

        public async Task<CreateWorkResponse> CreateWorkAsync(CreateWorkRequest request, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync("/internal/works", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Checker returned {(int)response.StatusCode}");
            }

            var dto = await response.Content.ReadFromJsonAsync<CreateWorkResponse>(ct);
            return dto ?? throw new InvalidOperationException("Empty response from Checker");
        }

        public async Task<List<WorkReportDto>> GetReportsForWorkAsync(Guid workId, CancellationToken ct = default)
        {
            var response = await _http.GetAsync($"/internal/works/{workId}/reports", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<WorkReportDto>();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Checker returned {(int)response.StatusCode}");
            }

            var list = await response.Content.ReadFromJsonAsync<List<WorkReportDto>>(ct);
            return list ?? new List<WorkReportDto>();
        }

        public async Task<AssignmentSummaryDto?> GetAssignmentSummaryAsync(string assignmentId,
            CancellationToken ct = default)
        {
            var response = await _http.GetAsync($"/internal/assignments/{assignmentId}/reports", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Checker returned {(int)response.StatusCode}");
            }

            var dto = await response.Content.ReadFromJsonAsync<AssignmentSummaryDto>(ct);
            return dto;
        }
    }

// DTO для multipart/form-data
    public sealed class UploadWorkForm
    {
        public IFormFile File { get; set; } = default!;

        public string StudentId { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;

        public string AssignmentId { get; set; } = string.Empty;
    }
}
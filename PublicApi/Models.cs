using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PublicApi
{
    /// <summary>Ответ публичного API при загрузке работы.</summary>
    public record PublicCreateWorkResponse(
        Guid WorkId,
        Guid ReportId,
        bool IsPlagiarism,
        string FileId);

    /// <summary>DTO отчёта о проверке работы для публичного API.</summary>
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

    public record FileTextDto(
        Guid WorkId,
        string FileId,
        string FileText
    );

    public record AssignmentSummaryDto(
        string AssignmentId,
        int TotalWorks,
        int PlagiarisedCount);

    public interface IFileGetTextClient
    {
        public Task<string> GetText(string workId, CancellationToken ct = default);
    }

    /// <summary>Клиент файлового хранилища, умеющий загружать файлы и получать их текст.</summary>
    public interface IFileStorageApiClient : IFileGetTextClient
    {
        Task<string> UploadAsync(IFormFile file, CancellationToken ct = default);
    }

    /// <summary>HTTP-клиент для обращения к микросервису FileStorage.</summary>
    public sealed class FileStorageApiClient(HttpClient http) : IFileStorageApiClient
    {
        public async Task<string> UploadAsync(IFormFile file, CancellationToken ct = default)
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(file.OpenReadStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "file", file.FileName);

            var response = await http.PostAsync("/internal/files", content, ct);
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

        public async Task<string> GetText(string fileId, CancellationToken ct = default)
        {
            try
            {
                var response = await http.GetAsync($"/internal/files/{fileId}", ct);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"FileStorage returned {(int)response.StatusCode}");
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);

                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException($"Не нашел файл: {ex.Message}");
            }
        }
    }

    /// <summary>Клиент сервиса Checker для создания работ и получения отчётов.</summary>
    /// <summary>Клиент сервиса Checker для создания работ и получения отчётов.</summary>
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
            return await ReadJsonOrThrow<CreateWorkResponse>(response, "POST /internal/works", ct);
        }

        public async Task<List<WorkReportDto>> GetReportsForWorkAsync(Guid workId, CancellationToken ct = default)
        {
            var response = await _http.GetAsync($"/internal/works/{workId}/reports", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<WorkReportDto>();
            }

            var list = await ReadJsonOrThrow<List<WorkReportDto>>(
                response,
                $"GET /internal/works/{workId}/reports",
                ct);

            return list ?? new List<WorkReportDto>();
        }

        public async Task<AssignmentSummaryDto?> GetAssignmentSummaryAsync(
            string assignmentId,
            CancellationToken ct = default)
        {
            var response = await _http.GetAsync($"/internal/assignments/{assignmentId}/reports", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            var dto = await ReadJsonOrThrow<AssignmentSummaryDto>(
                response,
                $"GET /internal/assignments/{assignmentId}/reports",
                ct);

            return dto;
        }

        // Общий хелпер: если код не 2xx — читаем тело и кидаем нормальное исключение
        private static async Task<T> ReadJsonOrThrow<T>(
            HttpResponseMessage response,
            string context,
            CancellationToken ct)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var msg =
                    $"Checker {context} failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}";
                throw new InvalidOperationException(msg);
            }

            var result = await response.Content.ReadFromJsonAsync<T>(ct);
            if (result is null)
            {
                throw new InvalidOperationException(
                    $"Checker {context} returned 200, но тело пустое или не удалось десериализовать");
            }

            return result;
        }
    }

    /// <summary>Модель формы для загрузки работы через multipart/form-data.</summary>
    public sealed class UploadWorkForm
    {
        public IFormFile File { get; set; } = null!;

        public string StudentId { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;

        public string AssignmentId { get; set; } = string.Empty;
    }
}
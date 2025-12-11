using System.Collections.Concurrent;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

var fileStorageUrl = builder.Configuration["FILESTORAGE_URL"]
                     ?? Environment.GetEnvironmentVariable("FILESTORAGE_URL")
                     ?? "ERROR";

builder.Services.AddHttpClient<IFileStorageClient, FileStorageClient>(client =>
{
    client.BaseAddress = new Uri(fileStorageUrl);
});

builder.Services.AddSingleton<IWorkStore, InMemoryWorkStore>();
builder.Services.AddSingleton<IPlagiarismDetector, PlagiarismDetector>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();


// === API моделей ===


// === Эндпоинты Checker ===

// Создание работы и отчёта (внутренний API)
// TODO Починить, сейчас не работает
app.MapPost("/internal/works", async (
        CreateWorkRequest request,
        IFileStorageClient storageClient,
        IWorkStore store,
        IPlagiarismDetector detector,
        CancellationToken ct) =>
    {
        var analyzeResult = await detector.AnalyzeAsync(
            request.AssignmentId,
            request.StudentId,
            request.FileId,
            storageClient,
            store,
            ct);

        // Повторно считаем хэш, чтобы сохранить его вместе с работой
        var bytes = await storageClient.GetFileBytesAsync(request.FileId, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var work = new WorkEntity(
            Guid.NewGuid(),
            request.StudentId,
            request.StudentName,
            request.AssignmentId,
            request.FileId,
            hash,
            DateTime.UtcNow);

        store.AddWork(work);

        var report = new ReportEntity(
            Guid.NewGuid(),
            work.WorkId,
            analyzeResult.isPlagiarism,
            analyzeResult.sourceWork?.WorkId,
            analyzeResult.score,
            DateTime.UtcNow);

        store.AddReport(report);

        return Results.Ok(new CreateWorkResponse(work.WorkId, report.ReportId, report.IsPlagiarism));
    })
    .WithName("CreateWorkInternal")
    .WithOpenApi().DisableAntiforgery();;

// Получить отчёты по конкретной работе
app.MapGet("/internal/works/{workId:guid}/reports", (Guid workId, IWorkStore store) =>
    {
        var reports = store.GetReportsForWork(workId).ToList();
        if (reports.Count == 0)
        {
            return Results.NotFound();
        }

        var allWorks = store.GetAllWorks().ToDictionary(w => w.WorkId);
        if (!allWorks.TryGetValue(workId, out var work))
        {
            return Results.NotFound();
        }

        var dtos = reports.Select(r => new WorkReportDto(
            r.ReportId,
            r.WorkId,
            work.StudentId,
            work.StudentName,
            work.AssignmentId,
            r.IsPlagiarism,
            r.SourceWorkId,
            r.PlagiarismScore,
            r.CreatedAt
        ));

        return Results.Ok(dtos);
    })
    .WithName("GetReportsForWorkInternal")
    .WithOpenApi().DisableAntiforgery();;

// Сводка по заданию
app.MapGet("/internal/assignments/{assignmentId}/reports", (string assignmentId, IWorkStore store) =>
    {
        var pairs = store.GetByAssignment(assignmentId).ToList();
        if (!pairs.Any())
        {
            return Results.NotFound();
        }

        var total = pairs.Select(p => p.Work.WorkId).Distinct().Count();
        var plagiarised = pairs.Count(p => p.Report.IsPlagiarism);

        var dto = new AssignmentSummaryDto(assignmentId, total, plagiarised);
        return Results.Ok(dto);
    })
    .WithName("GetAssignmentSummaryInternal")
    .WithOpenApi().DisableAntiforgery();;

app.MapGet("/health", () => Results.Ok("Checker OK"))
    .WithName("CheckerHealth")
    .WithOpenApi().DisableAntiforgery();;

app.Run();


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

public sealed class FileStorageClient : IFileStorageClient
{
    private readonly HttpClient _http;

    public FileStorageClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<byte[]> GetFileBytesAsync(string fileId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/internal/files/{fileId}", ct);
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
        return Convert.ToHexString(hash); // удобная текстовая форма
    }
}
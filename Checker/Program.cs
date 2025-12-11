using Checker;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

var fileStorageUrl = builder.Configuration["FILESTORAGE_URL"]
                     ?? throw new InvalidOperationException("Добавьте FILESTORAGE_URL в appsettings.Development.json");

builder.Services.AddHttpClient<IFileStorageClient, FileStorageClient>(client =>
{
    client.BaseAddress = new Uri(fileStorageUrl);
});

builder.Services.AddSingleton<IWorkStore, InMemoryWorkStore>();
builder.Services.AddSingleton<IPlagiarismDetector, PlagiarismDetector>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Title = "HSEHomeworkChecker Checker",
            Version = "v1",
            Description = "Микросервис проверки работ и формирования отчётов"
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// === api Checker ===

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
    .WithOpenApi().DisableAntiforgery();


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
    .WithOpenApi().DisableAntiforgery();


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
    .WithOpenApi().DisableAntiforgery();


app.MapGet("/status", () => Results.Ok("Checker OK"))
    .WithName("Checkerstatus")
    .WithOpenApi().DisableAntiforgery();


app.Run();
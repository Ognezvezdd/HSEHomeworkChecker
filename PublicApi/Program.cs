using Microsoft.AspNetCore.Mvc;
using PublicApi;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5000");

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// TODO: сюда позже добавлять HttpClient для вызова FileStorage и Checker
// builder.Services.AddHttpClient("FileStorage", ...);
// builder.Services.AddHttpClient("Checker", ...);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

// Внешний сценарий: студент загружает работу
// Теперь один параметр из тела: [FromForm] UploadWorkRequest request.
// Swagger видит ОДНО тело запроса и больше не падает.
app.MapPost("/api/works", async (
        [FromForm] UploadWorkRequest request) =>
    {
        if (request.File is null || request.File.Length == 0)
        {
            return Results.BadRequest("Файл не передан или пустой");
        }

        // TODO: здесь будет:
        // 1) вызов FileStorage: POST /internal/files → получить fileId
        // 2) вызов Checker: POST /internal/works → получить workId, reportId

        // Сейчас просто делаем фейковые id, чтобы endpoint работал.
        var fakeFileId = Guid.NewGuid(); // пока никуда не передаём, просто заглушка
        var fakeWorkId = Guid.NewGuid();
        var fakeReportId = Guid.NewGuid();

        var response = new PublicWorkCreatedResponse(
            fakeWorkId,
            fakeReportId,
            "Completed",
            DateTimeOffset.UtcNow);

        return Results.Ok(response);
    })
    .WithName("UploadWork")
    .WithOpenApi();

// Получить отчёты по работе (преподавательский сценарий)
// TODO: здесь позже дергать Checker: GET /internal/works/{id}/reports
app.MapGet("/api/works/{workId:guid}/reports", (Guid workId) =>
    {
        // Временно отдаём фейковый отчёт, чтобы маршрут работал.
        var fakeReport = new PublicReportDto(
            Guid.NewGuid(),
            workId,
            false,
            0.0,
            null,
            DateTimeOffset.UtcNow);

        return Results.Ok(new[] { fakeReport });
    })
    .WithName("GetWorkReports")
    .WithOpenApi();

// Простая аналитика по заданию
// TODO: здесь позже дергать Checker: GET /internal/assignments/{assignmentId}/reports
app.MapGet("/api/assignments/{assignmentId}/reports", (string assignmentId) =>
    {
        var summary = new PublicAssignmentSummaryDto(
            assignmentId,
            0,
            0);

        return Results.Ok(summary);
    })
    .WithName("GetAssignmentReports")
    .WithOpenApi();

app.MapGet("/api/status", () => Results.Ok("All OK!"))
    .WithName("GetStatus")
    .WithOpenApi();

app.Run();
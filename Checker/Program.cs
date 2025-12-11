using Checker;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5010");

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<InMemoryWorkStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

// Зарегистрировать работу и запустить "анализ"
app.MapPost("/internal/works", (CreateWorkRequest request, InMemoryWorkStore store) =>
    {
        // TODO
        // В реальности здесь:
        // 1) запросили бы файл у FileStorage по FileId
        // 2) прогнали алгоритм антиплагиата
        // 3) построили бы отчёт
        // Сейчас — простая заглушка.

        var workId = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        var work = new Work(
            workId,
            request.StudentId,
            request.StudentName,
            request.AssignmentId,
            request.FileId,
            DateTimeOffset.UtcNow);

        var report = new Report(
            reportId,
            workId,
            false, // TODO: реальный алгоритм
            0.0,
            null,
            DateTimeOffset.UtcNow);

        store.AddWork(work);
        store.AddReport(report);

        var response = new CreateWorkResponse(workId, reportId, "Completed", work.SubmittedAt);
        return Results.Ok(response);
    })
    .WithName("CreateWork")
    .WithOpenApi();

// Получить все отчёты по работе
app.MapGet("/internal/works/{workId:guid}/reports", (Guid workId, InMemoryWorkStore store) =>
    {
        var reports = store.GetReportsByWork(workId).ToArray();
        return Results.Ok(reports);
    })
    .WithName("GetReportsByWork")
    .WithOpenApi();

// Пример аналитики по заданию (очень упрощённо)
app.MapGet("/internal/assignments/{assignmentId}/reports", (string assignmentId, InMemoryWorkStore store) =>
    {
        var summary = store.GetAssignmentSummary(assignmentId);
        return Results.Ok(summary);
    })
    .WithName("GetAssignmentReportsSummary")
    .WithOpenApi();

app.MapGet("/api/status", () => Results.Ok("All OK!"))
    .WithName("GetStatus")
    .WithOpenApi();

app.Run();
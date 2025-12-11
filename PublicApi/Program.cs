using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using PublicApi;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);


var fileStorageUrl = builder.Configuration["FILESTORAGE_URL"]
                     ?? throw new InvalidOperationException("Добавьте FILESTORAGE_URL в appsettings.Development.json");


var checkerUrl = builder.Configuration["CHECKER_URL"]
                 ?? throw new InvalidOperationException("Добавьте CHECKER_URL в appsettings.Development.json");

builder.Services.AddHttpClient<IFileStorageApiClient, FileStorageApiClient>(client =>
{
    client.BaseAddress = new Uri(fileStorageUrl);
});
builder.Services.AddHttpClient<ICheckerApiClient, CheckerApiClient>(client =>
{
    client.BaseAddress = new Uri(checkerUrl);
});


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Title = "HSE AntiPlagiarism Public API",
            Version = "v1",
            Description = "Шлюз: принимает запросы от клиентов и проксирует их в FileStorage и Checker"
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Public API v1");
    });
}

app.UseHttpsRedirection();


// === Public API ===

app.MapPost("/api/works/submit", async (
        [FromForm] UploadWorkForm form,
        IFileStorageApiClient storageClient,
        ICheckerApiClient checkerClient,
        CancellationToken ct) =>
    {
        var file = form.File;

        if (file.Length == 0)
        {
            return Results.BadRequest("Empty file");
        }

        try
        {
            var fileId = await storageClient.UploadAsync(file, ct);

            var createRequest = new CreateWorkRequest(
                form.StudentId,
                form.StudentName,
                form.AssignmentId,
                fileId);

            var response = await checkerClient.CreateWorkAsync(createRequest, ct);

            var publicResponse = new PublicCreateWorkResponse(
                response.WorkId,
                response.ReportId,
                response.IsPlagiarism);

            return Results.Ok(publicResponse);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                title: "Внутренний сервис недоступен",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Внутренняя ошибка PublicApi при выполнении команды /api/works/submit",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .WithName("UploadWork")
    .WithSummary("Отправить работу на проверку")
    .WithDescription(
        "Принимает файл работы и метаданные (студент, задание), сохраняет файл и запускает анализ на плагиат.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .WithOpenApi()
    .DisableAntiforgery();

// Получить отчёты по конкретной работе: преподаватель
app.MapGet("/api/works/{workId:guid}/reports", async (
        Guid workId,
        ICheckerApiClient checkerClient,
        CancellationToken ct) =>
    {
        try
        {
            var reports = await checkerClient.GetReportsForWorkAsync(workId, ct);
            if (reports.Count == 0)
            {
                return Results.NotFound();
            }

            var dtos = reports.Select(r => new PublicWorkReportDto(
                r.ReportId,
                r.WorkId,
                r.StudentId,
                r.StudentName,
                r.AssignmentId,
                r.IsPlagiarism,
                r.SourceWorkId,
                r.PlagiarismScore,
                r.CreatedAt)).ToList();

            return Results.Ok(dtos);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                title: "Сервис проверки временно недоступен",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: $"Внутренняя ошибка PublicApi при выполнении команды /api/works/{workId:guid}/reports",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .WithName("GetWorkReport")
    .WithSummary("Получить отчёт по работе")
    .WithDescription("Возвращает JSON-отчёт по конкретной работе. Если Checker недоступен, вернёт 503.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status503ServiceUnavailable)
    .Produces(StatusCodes.Status500InternalServerError)
    .WithOpenApi().DisableAntiforgery();
;

app.MapGet("/api/assignments/{assignmentId}/reports", async (
        string assignmentId,
        ICheckerApiClient checkerClient,
        CancellationToken ct) =>
    {
        try
        {
            var summary = await checkerClient.GetAssignmentSummaryAsync(assignmentId, ct);
            if (summary is null)
            {
                return Results.NotFound();
            }

            var dto = new PublicAssignmentSummaryDto(
                summary.AssignmentId,
                summary.TotalWorks,
                summary.PlagiarisedCount);

            return Results.Ok(dto);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                title: "Сервис проверки временно недоступен",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: $"Внутренняя ошибка PublicApi при выполнении команды /api/assignments/{assignmentId}/reports",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .WithName("GetAssignmentSummary")
    .WithSummary("Сводка по заданию (assignment): преподаватель")
    .WithDescription("Возвращает JSON-отчёт для преподавателя. Если Checker недоступен, вернёт 503.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status503ServiceUnavailable)
    .Produces(StatusCodes.Status500InternalServerError)
    .WithOpenApi().DisableAntiforgery();

app.MapGet("/api/works/{workId}/wordcloud", async (
            [FromForm] UploadWorkForm form,
            IFileStorageApiClient storageClient,
            ICheckerApiClient checkerClient,
            CancellationToken ct) =>
        {
        }
    ).WithName("Wordcloud")
    .WithOpenApi().DisableAntiforgery();


app.MapGet("/status", () => Results.Ok("PublicApi OK"))
    .WithName("PublicApistatus")
    .WithOpenApi().DisableAntiforgery();


app.Run();
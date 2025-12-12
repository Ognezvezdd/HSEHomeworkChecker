using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi.Models;
using PublicApi;

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
    c.SwaggerDoc("v2",
        new OpenApiInfo
        {
            Title = "HSEHomeworkChecker Public API",
            Version = "v2",
            Description = "Шлюз: принимает запросы от клиентов и выполняет их через FileStorage и Checker"
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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
                response.IsPlagiarism,
                fileId);

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
                title: $"Внутренняя ошибка PublicApi при выполнении команды /api/works/{workId}/reports",
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


app.MapGet("/api/files/{fileId}/wordCloud", async (
        string fileId,
        IFileStorageApiClient storageClient,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct) =>
    {
        try
        {
            var text = await storageClient.GetText(fileId, ct);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Results.NotFound();
            }

            var queryParams = new Dictionary<string, string?> { ["text"] = text, ["format"] = "png" };

            var url = QueryHelpers.AddQueryString("https://quickchart.io/wordcloud", queryParams);
            var httpClient = httpClientFactory.CreateClient("chart");

            var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem(
                    title: "Не удалось получить картинку от QuickChart",
                    detail: $"Status code: {(int)response.StatusCode}",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var imageBytes = await response.Content.ReadAsByteArrayAsync(ct);
            return Results.File(imageBytes, "image/png");
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                title: "Сервис временно недоступен",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: $"Внутренняя ошибка PublicApi при выполнении команды /api/files/{fileId}/wordCloud",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .WithName("WordCloud")
    .WithSummary("Выдает картинку по самым частым словам")
    .WithDescription("Выдает картинку по самым частым словам")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status503ServiceUnavailable)
    .Produces(StatusCodes.Status500InternalServerError)
    .WithOpenApi()
    .DisableAntiforgery();


app.MapGet("/status", () => Results.Ok("PublicApi OK"))
    .WithName("PublicApiStatus")
    .WithOpenApi().DisableAntiforgery();


app.Run();
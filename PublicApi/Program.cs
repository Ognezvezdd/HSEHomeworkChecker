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
    c.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Title = "HSEHomeworkChecker Public API",
            Version = "v2",
            Description = "Принимает запросы от клиентов и выполняет их через FileStorage и Checker"
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/api/works/submit", PublicApiEndpoints.SubmitWorkAsync)
    .WithName("UploadWork")
    .WithSummary("Отправить работу на проверку")
    .WithDescription(
        "Принимает файл работы и метаданные (студент, задание), сохраняет файл и запускает анализ на плагиат.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .WithOpenApi()
    .DisableAntiforgery();

app.MapGet("/api/works/{workId:guid}/reports", PublicApiEndpoints.GetWorkReportsAsync)
    .WithName("GetWorkReport")
    .WithSummary("Получить отчёт по работе")
    .WithDescription("Возвращает JSON-отчёт по конкретной работе. Если Checker недоступен, вернёт 503.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status503ServiceUnavailable)
    .Produces(StatusCodes.Status500InternalServerError)
    .WithOpenApi().DisableAntiforgery();

app.MapGet("/api/assignments/{assignmentId}/reports", PublicApiEndpoints.GetAssignmentSummaryAsync)
    .WithName("GetAssignmentSummary")
    .WithSummary("Сводка по заданию (assignment): преподаватель")
    .WithDescription("Возвращает JSON-отчёт для преподавателя. Если Checker недоступен, вернёт 503.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status503ServiceUnavailable)
    .Produces(StatusCodes.Status500InternalServerError)
    .WithOpenApi().DisableAntiforgery();

app.MapGet("/api/files/{fileId}/wordCloud", PublicApiEndpoints.GetWordCloudAsync)
    .WithName("WordCloud")
    .WithSummary("Выдает картинку по самым частым словам")
    .WithDescription("Выдает картинку по самым частым словам")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status503ServiceUnavailable)
    .Produces(StatusCodes.Status500InternalServerError)
    .WithOpenApi()
    .DisableAntiforgery();

app.MapGet("/status", PublicApiEndpoints.GetStatus)
    .WithName("PublicApiStatus")
    .WithOpenApi().DisableAntiforgery();

app.Run();
using FileStorage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Настройки для загрузки больших файлов при необходимости
builder.Services.Configure<FormOptions>(opt =>
{
    opt.MultipartBodyLengthLimit = 1024L * 1024L * 100; // 100 MB
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Title = "HSEHomeworkChecker File Storage",
            Version = "v1",
            Description = "Микросервис хранения файлов (работ студентов)"
        });
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Простое файловое хранилище в папке ./storage
var storageRoot = Path.Combine(AppContext.BaseDirectory, "work_storage");
Directory.CreateDirectory(storageRoot);


// Внутренний api для других микросервисов: загрузка файла
app.MapPost("/internal/files", async (IFormFile file) =>
    {
        if (file.Length == 0)
        {
            return Results.BadRequest("Empty file");
        }

        var fileId = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(storageRoot, fileId);

        await using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream);
        }

        return Results.Ok(new FileUploadResponse(fileId));
    })
    .WithName("UploadFileInternal")
    .WithOpenApi().DisableAntiforgery();
;

// Внутренний api: получить содержимое файла (для Checker)
app.MapGet("/internal/files/{fileId}", async (string fileId) =>
    {
        var filePath = Path.Combine(storageRoot, fileId);
        if (!File.Exists(filePath))
        {
            return Results.NotFound();
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        return Results.File(bytes, "application/text/plain");
    })
    .WithName("GetFileInternal")
    .WithOpenApi().DisableAntiforgery();
;

app.MapGet("/status", () => Results.Ok("FileStorage OK"))
    .WithName("FileStoragestatus")
    .WithOpenApi().DisableAntiforgery();
;

app.Run();
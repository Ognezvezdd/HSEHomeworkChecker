using System.Text;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Настройки для загрузки больших файлов при необходимости
builder.Services.Configure<FormOptions>(opt =>
{
    opt.MultipartBodyLengthLimit = 1024L * 1024L * 100; // 100 MB
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Простое файловое хранилище в папке ./storage
var storageRoot = Path.Combine(AppContext.BaseDirectory, "storage");
Directory.CreateDirectory(storageRoot);


// Внутренний эндпоинт для других микросервисов: загрузка файла
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
    .WithOpenApi().DisableAntiforgery();;

// Внутренний эндпоинт: получить содержимое файла (для Checker)
app.MapGet("/internal/files/{fileId}", async (string fileId) =>
    {
        var filePath = Path.Combine(storageRoot, fileId);
        if (!File.Exists(filePath))
        {
            return Results.NotFound();
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        // Можно отдавать как text/plain, но для универсальности — octet-stream
        return Results.File(bytes, "application/octet-stream");
    })
    .WithName("GetFileInternal")
    .WithOpenApi().DisableAntiforgery();;

// Для проверки сервиса в Swagger (не обязательно использовать)
app.MapGet("/health", () => Results.Ok("FileStorage OK"))
    .WithName("FileStorageHealth")
    .WithOpenApi().DisableAntiforgery();;

app.Run();

// DTO для ответа при загрузке файла
public record FileUploadResponse(string FileId);
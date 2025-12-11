var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5020");

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

// Простое файловое хранилище: кладём файлы на диск в папку "storage"
var storageRoot = Path.Combine(AppContext.BaseDirectory, "storage");
Directory.CreateDirectory(storageRoot);

// Загрузка файла
app.MapPost("/internal/files", async (IFormFile file) =>
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest("Файл не передан или пустой");
        }

        var id = Guid.NewGuid();
        var extension = Path.GetExtension(file.FileName);
        var fileNameOnDisk = id + extension;
        var fullPath = Path.Combine(storageRoot, fileNameOnDisk);

        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var info = new StoredFileInfo(
            id,
            file.FileName,
            file.ContentType,
            file.Length,
            fullPath
        );

        return Results.Ok(info);
    })
    .WithName("UploadFile")
    .WithOpenApi();

// Получение файла по id
app.MapGet("/internal/files/{fileId:guid}", (Guid fileId) =>
    {
        var path = Directory
            .GetFiles(storageRoot)
            .FirstOrDefault(p =>
                Path.GetFileNameWithoutExtension(p).Equals(fileId.ToString(), StringComparison.OrdinalIgnoreCase));

        if (path is null)
        {
            return Results.NotFound();
        }

        var contentType = "application/octet-stream";
        var fileName = Path.GetFileName(path);

        var stream = File.OpenRead(path);
        return Results.File(stream, contentType, fileName);
    })
    .WithName("GetFile")
    .WithOpenApi();

app.MapGet("/api/status", () => Results.Ok("All OK!"))
    .WithName("GetStatus")
    .WithOpenApi();

app.Run();

public record StoredFileInfo(
    Guid Id,
    string OriginalName,
    string ContentType,
    long Size,
    string PathOnDisk);
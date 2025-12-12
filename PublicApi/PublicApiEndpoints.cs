using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace PublicApi
{
    /// <summary>
    /// Набор endpoint-обработчиков Public API для работы с домашними заданиями и файлами.
    /// </summary>
    public static class PublicApiEndpoints
    {
        /// <summary>
        /// Принимает файл работы и метаданные, сохраняет файл во внутреннем FileStorage
        /// и инициирует проверку работы на плагиат через сервис Checker.
        /// </summary>
        /// <param name="form">Форма multipart/form-data с файлом и данными студента/задания.</param>
        /// <param name="storageClient">HTTP-клиент для обращения к микросервису FileStorage.</param>
        /// <param name="checkerClient">HTTP-клиент для обращения к микросервису Checker.</param>
        /// <param name="ct">Токен отмены запроса.</param>
        /// <returns>
        /// 200 OK с <see cref="PublicCreateWorkResponse"/> при успешной отправке работы,
        /// 400 BadRequest при пустом файле, 503 или 500 при ошибках внутренних сервисов.
        /// </returns>
        public static async Task<IResult> SubmitWorkAsync(
            [FromForm] UploadWorkForm form,
            IFileStorageApiClient storageClient,
            ICheckerApiClient checkerClient,
            CancellationToken ct)
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
        }

        /// <summary>
        /// Возвращает список отчётов по конкретной работе (для преподавателя).
        /// Делегирует получение отчётов микросервису Checker.
        /// </summary>
        /// <param name="workId">Идентификатор работы, для которой нужно получить отчёты.</param>
        /// <param name="checkerClient">HTTP-клиент для обращения к микросервису Checker.</param>
        /// <param name="ct">Токен отмены запроса.</param>
        /// <returns>
        /// 200 OK c коллекцией <see cref="PublicWorkReportDto"/>, 404 если отчётов нет,
        /// 503 или 500 при ошибках сети/сервиса.
        /// </returns>
        public static async Task<IResult> GetWorkReportsAsync(
            Guid workId,
            ICheckerApiClient checkerClient,
            CancellationToken ct)
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
        }

        /// <summary>
        /// Возвращает агрегированную сводку по заданию: сколько работ загружено
        /// и сколько из них помечены как плагиат.
        /// </summary>
        /// <param name="assignmentId">Идентификатор задания (assignment), по которому строится сводка.</param>
        /// <param name="checkerClient">HTTP-клиент для обращения к микросервису Checker.</param>
        /// <param name="ct">Токен отмены запроса.</param>
        /// <returns>
        /// 200 OK с <see cref="PublicAssignmentSummaryDto"/>, 404 если по заданию нет данных,
        /// 503 или 500 при ошибках внутренних сервисов.
        /// </returns>
        public static async Task<IResult> GetAssignmentSummaryAsync(
            string assignmentId,
            ICheckerApiClient checkerClient,
            CancellationToken ct)
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
                    title:
                    $"Внутренняя ошибка PublicApi при выполнении команды /api/assignments/{assignmentId}/reports",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Строит облако слов по содержимому файла: читает текст из FileStorage
        /// и направляет запрос к внешнему сервису QuickChart для генерации изображения.
        /// </summary>
        /// <param name="fileId">Идентификатор файла в FileStorage.</param>
        /// <param name="storageClient">Клиент FileStorage для получения текста файла.</param>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов для обращения к QuickChart.</param>
        /// <param name="ct">Токен отмены запроса.</param>
        /// <returns>
        /// 200 OK с изображением PNG, 404 если файл пустой или не найден,
        /// 502 если QuickChart вернул ошибку, 503/500 при остальных сбоях.
        /// </returns>
        public static async Task<IResult> GetWordCloudAsync(
            string fileId,
            IFileStorageApiClient storageClient,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct)
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
        }

        /// <summary>
        /// Простая проверка endpoint для проверки доступности PublicApi.
        /// </summary>
        /// <returns>Строку-статус с HTTP 200 OK.</returns>
        public static IResult GetStatus()
        {
            return Results.Ok("PublicApi OK");
        }
    }
}
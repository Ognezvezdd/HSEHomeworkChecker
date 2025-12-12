# Homework №3 - Software Design (КПО)

## HSEHomeworkChecker

Автор: Капогузов Максим, БПИ-246

---

## Общая идея

Система принимает студенческие работы, сохраняет их, проверяет на плагиат и отдаёт отчёты/сводки преподавателю.

Архитектура микросервисов:

- **FileStorage** - отвечает только за хранение и выдачу файлов работ.
- **Checker** - отвечает за анализ работ на плагиат и хранение фактов сдачи/отчётов в СУБД.
- **PublicApi** - единая точка входа для клиентов (студент / преподаватель), маршрутизирует запросы в FileStorage и
  Checker, а также строит облако слов по тексту работы.

Каждый сервис — отдельный ASP.NET Core Minimal API с включённым Swagger.

---

## Зависимости

- .NET 8 SDK
- Swashbuckle.AspNetCore (Swagger)
- Dapper
- Microsoft.Data.Sqlite (СУБД внутри Checker)
- Docker и Docker Compose

---

## Структура репозитория

В корне:

- `docker-compose.yml` — поднятие всех микросервисов одной командой.
- `FileStorage/` — микросервис хранения файлов.
- `Checker/` — микросервис анализа и отчётов (с SQLite-базой).
- `PublicApi/` — шлюз для внешних запросов.

### FileStorage

- `FileStorage.csproj`
- `Dockerfile`
- `Program.cs` — Minimal API:
    - Swagger;
    - загрузка файла (`POST /internal/files`);
    - выдача файла по идентификатору (`GET /internal/files/{fileId}`).

Файлы хранятся в локальной папке `work_storage` внутри контейнера
(простое файловое хранилище на диске; при желании можно заменить на S3 хранилище).

### Checker

- `Checker.csproj`
- `Dockerfile`
- `Program.cs` — Minimal API:
    - Swagger;
    - `HttpClient` для доступа к FileStorage;
    - `IWorkStore` с реализацией `SqliteWorkStore` (SQLite + Dapper);
    - сервис определения плагиата `IPlagiarismDetector`.

Checker получает байты файла из FileStorage, считает SHA-256 хеш, ищет совпадения по тому же заданию и другому студенту,
сохраняет факт сдачи работы и отчёт о проверке в SQLite (таблицы `Works` и `Reports`).

### PublicApi

- `PublicApi.csproj`
- `Dockerfile`
- `Program.cs` — API Gateway:
    - Swagger;
    - `HttpClient` к FileStorage и Checker;
    - публичный endpoint для студента: `POST /api/works/submit` (загрузка работы и запуск проверки);
    - публичные endpoint’ы для преподавателя:
        - `GET /api/works/{workId}/reports` — отчёты по конкретной работе;
        - `GET /api/assignments/{assignmentId}/reports` — сводка по заданию;
    - `GET /api/files/{fileId}/wordCloud` — запрос к QuickChart для визуализации работы в виде облака слов.

Все внешние запросы идут только в PublicApi; прямого доступа к FileStorage и Checker снаружи нет.
---

## Публичные HTTP-методы (PublicApi)

Основные методы, которыми пользуется клиент:

1) Отправка работы на проверку (студент)

- Метод: POST `/api/works/submit`
- Тело: multipart/form-data
    - file - файл работы;
    - studentId - идентификатор студента;
    - studentName - ФИО или имя студента;
    - assignmentId - идентификатор задания.
- Ответ 200 OK:

```
  {
  "workId": "...",
  "reportId": "...",
  "isPlagiarism": true/false,
  "fileId": "..."
  }
  ```

PublicApi:

- загружает файл в FileStorage (POST `/internal/files`),
- получает fileId,
- вызывает Checker (POST `/internal/works`) с метаданными и fileId,
- возвращает клиенту workId + reportId + флаг плагиата.

2) Отчёты по конкретной работе (преподаватель)

- Метод: GET `/api/works/{workId}/reports`
- Ответ 200 OK: список отчётов по данной сдаче

```
  {
  "reportId": "...",
  "workId": "...",
  "studentId": "...",
  "studentName": "...",
  "assignmentId": "...",
  "isPlagiarism": true/false,
  "sourceWorkId": "... или null",
  "plagiarismScore": число,
  "createdAt": "..."
  }
```

PublicApi вызывает Checker:

- GET `/internal/works/{workId}/reports `
  Checker читает данные из IWorkStore и отдаёт отчёты.

3) Сводка по заданию (преподаватель)

- Метод: GET `/api/assignments/{assignmentId}/reports`
- Ответ 200 OK:
- ```
  {
  "assignmentId": "...",
  "totalWorks": число,
  "plagiarisedCount": число
  }
  ```

PublicApi вызывает Checker:

- GET `/internal/assignments/{assignmentId}/reports`

4) Облако слов по файлу

- Метод: GET `/api/files/{fileId}/wordCloud`
- Ответ 200 OK: PNG-картинка (image/png)
- `fileId` Можно получить при отправке файла (`/api/works/submit`)

PublicApi:

- получает текст работы через FileStorageApiClient (по fileId),
- делает запрос к QuickChart (`https://quickchart.io/wordcloud?text=...&format=png`),
- возвращает клиенту картинку.

## Обработка ошибок

- PublicApi ловит HttpRequestException при вызове FileStorage и Checker
  и возвращает:
    - 503 Service Unavailable - если внутренний сервис недоступен;
    - 500 Internal Server Error - при неожиданных ошибках.
- Если данные не найдены, PublicApi и Checker возвращают 404 Not Found.

Таким образом, падение одного микросервиса не ломает весь Gateway:
клиент получает понятный HTTP-статус
---

## Алгоритм определения плагиата (Checker)

Checker для каждой работы хранит:

- studentId;
- studentName;
- assignmentId;
- fileId;
- hash содержимого файла (SHA256);
- время сдачи.

Плагиат считается обнаруженным, если:

- по тому же assignmentId существует более ранняя работа другого студента
  с тем же hash файла.

Отчёт содержит:

```
- workId;
- assignmentId;
- studentId и studentName;
- isPlagiarism (true/false);
- sourceWorkId (идентификатор «первой» работы, если плагиат найден);
- plagiarismScore (100 при точном совпадении);
- время создания отчёта.
```

---

## Swagger / Postman

Для каждого сервиса настроен Swagger (Swashbuckle). Через Swagger-UI можно прогнать все сценарии без Postman.

- **PublicApi**
    - Swagger (локально): `http://localhost:5050/swagger`
    - Основные публичные методы:
        - `GET /status`
        - `POST /api/works/submit`
        - `GET /api/works/{workId}/reports`
        - `GET /api/assignments/{assignmentId}/reports`
        - `GET /api/files/{fileId}/wordCloud`

- **FileStorage**
    - Swagger: `http://localhost:5020/swagger`
    - Внутренние методы хранения файлов:
        - `GET /status`
        - `POST /internal/files` — загрузка файла, возвращает `fileId`
        - `GET /internal/files/{fileId}` — получить бинарное содержимое файла
        - `GET /internal/FirstFile` — вспомогательный эндпоинт для отладки (первый файл из хранилища)

- **Checker**
    - Swagger: `http://localhost:5010/swagger`
    - Внутренние методы анализа и отчётов:
        - `GET /status`
        - `POST /internal/works` — зафиксировать факт сдачи работы и сформировать отчёт
        - `GET /internal/works/{workId}/reports` — получить отчёты по конкретной работе
        - `GET /internal/assignments/{assignmentId}/reports` — агрегированная сводка по заданию

Swagger-UI покрывает требование «коллекция Postman / Swagger, демонстрирующая функциональность всех API».
При желании из Swagger можно сгенерировать Postman-коллекцию.
---

## Docker и запуск

Все три сервиса упакованы в Docker-образы и поднимаются через `docker-compose`

Основные порты на хосте:

- FileStorage: http://localhost:5020
- Checker: http://localhost:5010
- PublicApi: http://localhost:5050

Переменные окружения (используются в коде):

- Для Checker:
    - FILESTORAGE_URL=http://file-storage:8080
- Для PublicApi:
    - FILESTORAGE_URL=http://file-storage:8080
    - CHECKER_URL=http://checker:8080
      Эти переменные хранятся в `appsettings.Development.json` в каждом проекте отдельно

    - Пример `appsettings.Development.json`:

```json
{
  "FILESTORAGE_URL": "http://localhost:5020"
}
```

## Технические сценарии взаимодействия микросервисов (Просто flow)

### 1. Студент отправляет работу на проверку

1. Клиент вызывает `POST /api/works/submit` (PublicApi), передаёт:
    - файл (`multipart/form-data`),
    - `studentId`, `studentName`, `assignmentId`.
2. PublicApi:
    - отправляет файл в FileStorage: `POST /internal/files`;
    - получает `fileId`;
    - отправляет запрос в Checker: `POST /internal/works` с данными студента, задания и `fileId`.
3. Checker:
    - скачивает файл из FileStorage: `GET /internal/files/{fileId}`;
    - считает SHA-256 хеш содержимого;
    - ищет в своей БД (SQLite) более ранние работы с тем же `assignmentId` и тем же хешем, но другим `studentId`;
    - сохраняет новую запись в таблицу `Works` (факт сдачи работы);
    - сохраняет запись в таблицу `Reports` (отчёт по проверке).
4. PublicApi возвращает клиенту:
    - `workId`,
    - `reportId`,
    - `isPlagiarism`,
    - `fileId`.

### 2. Преподаватель запрашивает отчёты по работе

1. Клиент вызывает `GET /api/works/{workId}/reports` (PublicApi).
2. PublicApi вызывает Checker: `GET /internal/works/{workId}/reports`.
3. Checker читает данные из таблиц `Works` и `Reports` и возвращает список отчётов.
4. PublicApi мапит внутренние DTO в `PublicWorkReportDto` и отдаёт список клиенту.

### 3. Преподаватель запрашивает сводку по заданию

1. Клиент вызывает `GET /api/assignments/{assignmentId}/reports` (PublicApi).
2. PublicApi вызывает Checker: `GET /internal/assignments/{assignmentId}/reports`.
3. Checker агрегирует данные по таблицам `Works` и `Reports`:
    - считает общее число работ по заданию,
    - считает количество работ, где `IsPlagiarism = 1`.
4. PublicApi возвращает агрегированную сводку (`assignmentId`, `totalWorks`, `plagiarisedCount`).

### 4. Облако слов по файлу

1. Клиент вызывает `GET /api/files/{fileId}/wordCloud` (PublicApi).
2. PublicApi:
    - получает текст файла через FileStorage:
      `GET /internal/files/{fileId}` → байты → UTF-8 строка;
    - отправляет запрос в QuickChart:
      `GET https://quickchart.io/wordcloud?text=...&format=png`;
    - возвращает клиенту PNG-картинку.

## Интеграционные тесты (tests.py)

В папке Tests лежит скрипт `tests.py`, который прогоняет интеграционные тесты для всей системы.

Тесты проверяют:

1. Доступность всех микросервисов:
    - `GET /status` у FileStorage, Checker и PublicApi.
2. Полный сценарий отправки и проверки работ:
    - `POST /api/works/submit` для трёх разных работ:
        - первая работа по заданию — не плагиат;
        - вторая работа с тем же содержимым и тем же заданием, но другим студентом — плагиат;
        - третья работа с тем же содержимым, но другим заданием — не плагиат.
    - `GET /api/works/{workId}/reports` — по каждой работе должен быть хотя бы один отчёт,
      по второй работе — отчёт с `isPlagiarism = true`.
    - `GET /api/assignments/{assignmentId}/reports` — сводка по заданию (минимум 2 работы, минимум 1 плагиат).
3. Построение облака слов:
    - `GET /api/files/{fileId}/wordCloud` — возвращает PNG-картинку (`Content-Type: image/png`) для загруженного файла.

Для запуска тестов:

1. Поднять систему через Docker:

```bash
docker compose up --build
```

2. Установить зависимости для Python:

```bash
pip install requests
```

3. Запустить тесты:

```bash
python3 tests.py
```

При успешном прохождении скрипт выводит ВСЕ ТЕСТЫ ПРОШЛИ УСПЕШНО ✅.
Если какой-то api возвращает неожиданный статус/ответ, тесты падают с подробным сообщением об ошибке.

## Запуск:

```bash
docker compose up --build
```

Если образы уже собраны:

```bash
docker compose up
```

Остановка:

```bash
docker compose down
```
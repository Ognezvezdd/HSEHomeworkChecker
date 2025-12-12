# Homework №3 - Software Design (КПО)

## HSEHomeworkChecker

Автор: Капогузов Максим, БПИ-246

---

## Общая идея

Система принимает студенческие работы, сохраняет их, проверяет на плагиат и отдаёт отчёты/сводки преподавателю.

Архитектура микросервисов:

- **FileStorage** - отвечает только за хранение и выдачу файлов работ.
- **Checker** - отвечает за анализ работ на плагиат и хранение фактов сдачи/отчётов в СУБД.
- **PublicApi** - единая точка входа для клиентов (студент / преподаватель), маршрутизирует запросы в FileStorage и Checker, а также строит облако слов по тексту работы.

Каждый сервис — отдельный ASP.NET Core Minimal API с включённым Swagger.

---

## Зависимости

- .NET 8 SDK
- Swashbuckle.AspNetCore (Swagger)
- Dapper + Microsoft.Data.Sqlite (СУБД внутри Checker)
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

Файлы хранятся в локальной папке `work_storage` внутри контейнера (простое файловое хранилище без S7).

### Checker

- `Checker.csproj`
- `Dockerfile`
- `Program.cs` — Minimal API:
    - Swagger;
    - `HttpClient` для доступа к FileStorage;
    - `IWorkStore` с реализацией `SqliteWorkStore` (SQLite + Dapper);
    - сервис определения плагиата `IPlagiarismDetector`.

Checker получает байты файла из FileStorage, считает SHA-256 хеш, ищет совпадения по тому же заданию и другому студенту, сохраняет факт сдачи работы и отчёт о проверке в SQLite (таблицы `Works` и `Reports`).

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

- Метод: POST /api/works/submit
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

- загружает файл в FileStorage (POST /internal/files),
- получает fileId,
- вызывает Checker (POST /internal/works) с метаданными и fileId,
- возвращает клиенту workId + reportId + флаг плагиата.

2) Отчёты по конкретной работе (преподаватель)

- Метод: GET /api/works/{workId}/reports
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

- GET /internal/works/{workId}/reports  
  Checker читает данные из IWorkStore и отдаёт отчёты.

3) Сводка по заданию (преподаватель)

- Метод: GET /api/assignments/{assignmentId}/reports
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

Для каждого сервиса настроен Swagger:

- **PublicApi**:
    - URL (локально): `http://localhost:5050/swagger`
    - показывает:
        - `POST /api/works/submit`
        - `GET /api/works/{workId}/report`
        - `GET /api/works/{workId}/wordcloud`
- **FileStorage**:
    - URL: `http://localhost:5020/swagger`
    - внутренние эндпоинты:
        - `POST /internal/works`
        - `GET /internal/works/{workId}/content`
- **Checker**:
    - URL: `http://localhost:5010/swagger`
    - внутренние эндпоинты:
        - `POST /internal/works/{workId}/analyze`
        - `GET /internal/works/{workId}/report`
        - `GET /internal/works/{workId}/wordcloud-url`

Swagger-UI для каждого сервиса позволяет вручную прогнать все сценарии.

При желании на основе Swagger можно собрать Postman-коллекцию, но уже сам Swagger покрывает требование
«коллекция/документация, демонстрирующая функциональность».

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

Запуск:

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

### Проверка по требованиям

1. Реализация основных требований (2 балла)
    - Отправка работы на проверку (с фиксацией метаданных и файла).
    - Анализ работы и формирование отчёта.
    - Получение отчётов по работе.
2. Определение функциональности по микросервисам (2 балла)
    - Есть два бизнес-сервиса (FileStorage, Checker) и сервис-посредник (PublicApi).
    - Отдельные зоны ответственности:
    - Storage - файлы;
    - Checker - анализ;
    - PublicApi - маршрутизация и внешний контракт.
3. Использование контейнеризации (2 балла)
    - У каждого микросервиса есть Dockerfile.
    - Есть общий docker-compose.yml, который поднимает всё командой docker compose up.
    - Настроены порты и сеть.
4. Документация (1 балл)
    - Swagger настроен во всех сервисах.
    - В README описана архитектура и пользовательские/технические сценарии.
5. Качество кода (2 балла)
    - Явное разделение ответственности по сервисам.
    - Взаимодействие идёт только через HTTP, без shared-базы.
    - Обработка ошибок межсервисного взаимодействия (503, 404, 500).
    - Имя сервисов и api отражает их назначение.
6. Требование на 10 баллов - облако слов (1 балл)
    - Реализован сценарий генерации URL облака слов через QuickChart.
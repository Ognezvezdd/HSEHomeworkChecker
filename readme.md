# Homework №3 - Software Design (КПО)

## HSEHomeworkChecker

### Author: Капогузов Максим, БПИ-246

---

## Общая идея

Система проверяет студенческие работы на плагиат и хранит результаты.  
Архитектура микросервисная:

- **FileStorage** - отвечает только за хранение и выдачу файлов работ.
- **Checker** - анализирует работы, определяет плагиат, хранит и отдаёт отчёты.
- **PublicApi** - единая точка входа для клиентов, маршрутизирует запросы к FileStorage и Checker.

Каждый сервис - отдельное ASP.NET Minimal API-приложение с включённым Swagger.

---

## Зависимости

Общие для всех сервисов:

- .NET 8 SDK
- `Swashbuckle.AspNetCore` (Swagger/OpenAPI)
- `System.Net.Http` (встроен в .NET, используется через `HttpClient`)

Docker:

- Docker
- Docker Compose

---

## Структура репозитория

Корень проекта:

- `docker-compose.yml` - поднимает все микросервисы одной командой.
- `FileStorage/` - микросервис хранения файлов.
- `Checker/` - микросервис анализа работ и отчётов.
- `PublicApi/` - API Gateway для внешних клиентов.
- `.gitignore`

### FileStorage/

- `FileStorage.csproj`
- `Dockerfile`
- `Program.cs` - минимальное Web API:
    - настроенный Swagger;
    - эндпоинты для загрузки и выдачи файлов (работ).
- `StorageSettings`/константы пути - базовая конфигурация хранения (например, локальная папка `./data`).

Роль: принимает файл и метаданные (кто сдал, по какому заданию), присваивает `workId`, сохраняет файл и позволяет его получить по этому `workId`.

### Checker/

- `Checker.csproj`
- `Dockerfile`
- `Program.cs` - минимальное Web API:
    - настроенный Swagger;
    - `HttpClient` к FileStorage;
    - эндпоинты:
        - для запуска анализа работы;
        - для получения отчёта по работе;
        - для генерации ссылки на облако слов (QuickChart) по тексту работы.

Роль: по `workId` получает содержимое работы из FileStorage, вычисляет метрики и сохраняет отчёт (пока можно хранить in-memory, так как цель - продемонстрировать взаимодействие).

Алгоритм определения плагиата (описанный в README, реализация может быть упрощённой):

- для каждой работы учитываем:
    - `studentId`,
    - `assignmentId`,
    - текст (или нормализованный hash текста).
- плагиат считается обнаруженным, если:
    - по тому же `assignmentId` уже существует **более ранняя** работа **другого студента** с идентичным текстом (или совпадающим hash).
- отчёт содержит:
    - `workId`,
    - `assignmentId`,
    - `studentId`,
    - `isPlagiarism` (`true/false`),
    - возможные дополнительные поля (timestamp проверки, найденный «оригинал» и т. п.).

### PublicApi/

- `PublicApi.csproj`
- `Dockerfile`
- `Program.cs` - минимальный API Gateway:
    - настроенный Swagger;
    - `HttpClient("FileStorage")` - для общения с FileStorage;
    - `HttpClient("Checker")` - для общения с Checker;
    - публичные эндпоинты верхнего уровня.

Роль: **единственная точка входа** для клиента (Postman/браузер/фронтенд).  
Весь внешний трафик идёт только через PublicApi.

---

## Пользовательские сценарии и технический флоу

### 1. Отправка работы на проверку

**Пользовательский сценарий:**

Студент отправляет свою работу на проверку, указывая, кто он и по какому заданию сдаёт.

**Публичный запрос (PublicApi):**

- `POST /api/works/submit`
    - `multipart/form-data`:
        - `file` - загружаемый файл (работа);
        - `studentId` - идентификатор студента;
        - `assignmentId` - идентификатор задания.

**Технический флоу:**

1. **PublicApi** принимает запрос, вытаскивает файл и метаданные.
2. PublicApi по `HttpClient("FileStorage")` вызывает внутренний эндпоинт FileStorage, например:
    - `POST http://file-storage:8080/internal/works`
    - в теле передаёт файл + `studentId`, `assignmentId`.
3. **FileStorage**:
    - сохраняет файл локально (например, `./data/{workId}.bin`);
    - записывает метаданные (хотя бы в памяти) и возвращает `workId`.
4. Получив `workId`, **PublicApi** вызывает **Checker**:
    - `POST http://checker:8080/internal/works/{workId}/analyze`
    - передаёт `workId`.
5. **Checker**:
    - по `workId` запрашивает текст/контент работы у FileStorage:
        - `GET http://file-storage:8080/internal/works/{workId}/content`;
    - запускает алгоритм определения плагиата;
    - сохраняет отчёт (например, in-memory словарь `workId -> Report`).
6. PublicApi возвращает клиенту:
    - `200 OK` и JSON вида:
      ```json
      {
        "workId": "....",
        "message": "Работа принята, анализ запущен"
      }
      ```

В Swagger PublicApi этот сценарий описан эндпоинтом `POST /api/works/submit` c Summary/Description.

---

### 2. Получение отчётов по работе

**Пользовательский сценарий:**

Преподаватель хочет посмотреть аналитику по конкретной работе/сдаче.

**Публичный запрос (PublicApi):**

- `GET /api/works/{workId}/report`

**Технический флоу:**

1. **PublicApi** принимает `workId` в маршруте.
2. PublicApi делает запрос к **Checker**:
    - `GET http://checker:8080/internal/works/{workId}/report`
3. **Checker**:
    - находит сохранённый отчёт,
    - возвращает JSON:
      ```json
      {
        "workId": "....",
        "studentId": "....",
        "assignmentId": "....",
        "isPlagiarism": true,
        "checkedAt": "..."
      }
      ```
4. PublicApi проксирует ответ клиенту (при необходимости добавляя свою обёртку или просто отдавая JSON как есть).

Если Checker недоступен или упал, PublicApi ловит `HttpRequestException` и возвращает:

- `503 Service Unavailable` с понятным сообщением.

---

### 3. Облако слов по работе (требование на 10 баллов)

**Пользовательский сценарий:**

Преподаватель хочет визуально оценить содержание работы - получить облако слов.

**Публичный запрос (PublicApi):**

- `GET /api/works/{workId}/wordcloud`

**Технический флоу:**

1. PublicApi принимает `workId`.
2. PublicApi вызывает **Checker**:
    - `GET http://checker:8080/internal/works/{workId}/wordcloud-url`
3. **Checker**:
    - получает текст работы из FileStorage (`GET /internal/works/{workId}/content`);
    - нормализует текст (убирает переводы строк и т. п.);
    - кодирует в URL (например, через `UrlEncode`);
    - формирует ссылку для QuickChart:
        - `https://quickchart.io/wordcloud?text={encodedText}`;
    - возвращает JSON:
      ```json
      {
        "workId": "....",
        "wordCloudUrl": "https://quickchart.io/wordcloud?text=..."
      }
      ```
4. PublicApi отдаёт этот JSON клиенту.

Клиент может открыть `wordCloudUrl` в браузере и получить значок облака слов.

---

## Обработка ошибок межсервисного взаимодействия

Во всех вызовах `HttpClient` (PublicApi → FileStorage/Checker, Checker → FileStorage) используются `try/catch`:

- Если целевой сервис недоступен (нет сети, контейнер не поднят, 5xx), вызывающая сторона не падает, а возвращает:
    - **503 Service Unavailable** - с сообщением вида:
        - «Сервис проверки временно недоступен»
        - «Сервис хранилища недоступен»
- Если данные не найдены (нет работы/отчёта), возвращается:
    - **404 Not Found** - с понятным `message`.
- При неожиданных исключениях:
    - **500 Internal Server Error** - с коротким описанием для отладки.

Таким образом, критерий «обработка ошибок (один из микросервисов упал)» выполняется на уровне PublicApi и Checker.

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

При желании на основе Swagger можно собрать Postman-коллекцию, но уже сам Swagger покрывает требование «коллекция/документация, демонстрирующая функциональность».

---

## Docker и запуск через docker-compose

### Порты и сервисы

`docker-compose.yml` поднимает три контейнера:

- `file-storage`:
    - образ из `FileStorage/Dockerfile`;
    - порт внутри контейнера: `8080`;
    - опубликован на хосте как `5020:8080`;
    - `ASPNETCORE_URLS=http://0.0.0.0:8080`.
- `checker`:
    - образ из `Checker/Dockerfile`;
    - порт: `8080`;
    - опубликован на `5010:8080`;
    - переменные окружения:
        - `FILESTORAGE_URL=http://file-storage:8080`.
- `public-api`:
    - образ из `PublicApi/Dockerfile`;
    - порт: `8080`;
    - опубликован на `5050:8080`;
    - переменные окружения:
        - `FILESTORAGE_URL=http://file-storage:8080`;
        - `CHECKER_URL=http://checker:8080`.

Все сервисы подключены к общей сети `app-net`.

### Команды

Сборка и запуск всей системы:

```bash
docker compose up --build
```
Или просто запуск:

```bash
docker compose up
```
После успешного запуска:
- PublicApi: http://localhost:5050/swagger
- FileStorage: http://localhost:5020/swagger
- Checker: http://localhost:5010/swagger

Остановка:
```bash
docker compose down
```

---

### Архитектура в терминах критериев ДЗ
1. Реализация основных требований (2 балла)
   - Отправка работы на проверку (с фиксацией метаданных и файла).
   - Анализ работы и формирование отчёта.
   - Получение отчётов по работе.
2. Определение функциональности по микросервисам (2 балла)
   - Есть минимум два бизнес-сервиса (FileStorage, Checker) и сервис-посредник (PublicApi).
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
   - Имя сервисов и эндпоинтов отражает их назначение.
6. Требование на 10 баллов - облако слов (1 балл)
   - Реализован сценарий генерации URL облака слов через QuickChart.
   - Есть публичный endpoint GET /api/works/{workId}/wordcloud.
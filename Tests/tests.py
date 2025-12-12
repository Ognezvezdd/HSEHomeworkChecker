"""
Простой скрипт для интеграционных тестов микросервисов HSEHomeworkChecker.

Что он проверяет:
1) /status у всех трёх сервисов (FileStorage, Checker, PublicApi)
2) Полный сценарий:
   - загрузка работы через PublicApi (/api/works/submit)
   - проверка флага плагиата (вторая такая же работа = плагиат)
   - получение отчёта по работе (/api/works/{workId}/reports)
   - получение сводки по заданию (/api/assignments/{assignmentId}/reports)
   - получение word cloud по файлу (/api/files/{fileId}/wordCloud)
3) ДОПОЛНИТЕЛЬНО:
   - проверка, что у плагиат-работы заполнен sourceWorkId и plagiarismScore
   - проверка 404 для несуществующей работы
   - проверка 404 для несуществующего задания

Перед запуском:
- docker compose up --build
"""

import os
import uuid
from typing import Any, Dict, List

import requests

# ==== базовые URL-ы (можно переопределить через переменные окружения) ====
PUBLIC_API_URL = os.getenv("PUBLIC_API_URL", "http://localhost:5050")
CHECKER_URL = os.getenv("CHECKER_URL", "http://localhost:5010")
FILE_STORAGE_URL = os.getenv("FILE_STORAGE_URL", "http://localhost:5020")


class ApiError(Exception):
    pass


def assert_status(resp: requests.Response, expected: int) -> None:
    if resp.status_code != expected:
        raise ApiError(
            f"Ожидали статус {expected}, получили {resp.status_code}. "
            f"URL={resp.request.method} {resp.request.url}, body={resp.text}"
        )


def test_health_checks(session: requests.Session) -> None:
    print("== Проверка /status для всех сервисов ==")
    for name, base in [
        ("FileStorage", FILE_STORAGE_URL),
        ("Checker", CHECKER_URL),
        ("PublicApi", PUBLIC_API_URL),
    ]:
        url = f"{base}/status"
        resp = session.get(url)
        assert_status(resp, 200)
        print(f"  OK: {name} /status => {resp.text.strip()}")


def submit_work(
    session: requests.Session,
    student_id: str,
    student_name: str,
    assignment_id: str,
    content: str,
) -> Dict[str, Any]:
    """Отправка работы через PublicApi /api/works/submit."""
    url = f"{PUBLIC_API_URL}/api/works/submit"

    files = {
        "File": ("work.txt", content.encode("utf-8"), "text/plain"),
    }
    data = {
        "StudentId": student_id,
        "StudentName": student_name,
        "AssignmentId": assignment_id,
    }

    resp = session.post(url, files=files, data=data)
    assert_status(resp, 200)
    body = resp.json()

    for key in ["workId", "reportId", "isPlagiarism", "fileId"]:
        if key not in body:
            raise ApiError(f"/api/works/submit: в ответе нет поля '{key}': {body}")

    print(
        f"  submit_work: assignment={assignment_id}, student={student_id}, "
        f"isPlagiarism={body['isPlagiarism']}"
    )
    return body


def get_work_reports(session: requests.Session, work_id: str) -> List[Dict[str, Any]]:
    """Обёртка над GET /api/works/{workId}/reports через PublicApi."""
    url = f"{PUBLIC_API_URL}/api/works/{work_id}/reports"
    r = session.get(url)
    if r.status_code == 404:
        return []
    assert_status(r, 200)
    body = r.json()
    if not isinstance(body, list):
        raise ApiError(f"/api/works/{work_id}/reports вернул не список: {body}")
    return body


def test_submit_and_reports(session: requests.Session) -> Dict[str, Any]:
    print("== Проверка сценария отправки и получения отчётов ==")

    assignment_id = f"test-{uuid.uuid4()}"
    assignment_id2 = f"{assignment_id}-other"

    text_common = "This is a test file used to check plagiarism logic. Hello HSE!"

    resp1 = submit_work(
        session,
        student_id="student_1",
        student_name="Alice",
        assignment_id=assignment_id,
        content=text_common,
    )
    work1_id = resp1["workId"]
    file1_id = resp1["fileId"]
    assert resp1["isPlagiarism"] is False, "Первая работа не должна считаться плагиатом"

    resp2 = submit_work(
        session,
        student_id="student_2",
        student_name="Bob",
        assignment_id=assignment_id,
        content=text_common,
    )
    work2_id = resp2["workId"]
    file2_id = resp2["fileId"]
    assert resp2["isPlagiarism"] is True, (
        "Вторая такая же работа (другой студент, то же задание) должна считаться плагиатом"
    )

    resp3 = submit_work(
        session,
        student_id="student_3",
        student_name="Carol",
        assignment_id=assignment_id2,
        content=text_common,
    )
    work3_id = resp3["workId"]
    file3_id = resp3["fileId"]
    assert resp3["isPlagiarism"] is False, (
        "Та же работа, но другое задание — не должна считаться плагиатом в рамках другого assignment"
    )

    reports1 = get_work_reports(session, work1_id)
    reports2 = get_work_reports(session, work2_id)
    reports3 = get_work_reports(session, work3_id)

    assert len(reports1) >= 1, "По первой работе должен быть хотя бы один отчёт"
    assert len(reports2) >= 1, "По второй работе должен быть хотя бы один отчёт"
    assert len(reports3) >= 1, "По третьей работе должен быть хотя бы один отчёт"

    assert any(r["isPlagiarism"] for r in reports2), "Во втором отчёте должен быть флаг плагиата"

    print("  OK: отчёты по работам возвращаются корректно")

    # === Проверяем сводку по заданию ===

    url_summary = f"{PUBLIC_API_URL}/api/assignments/{assignment_id}/reports"
    r_sum = session.get(url_summary)
    assert_status(r_sum, 200)
    summary = r_sum.json()

    for key in ["assignmentId", "totalWorks", "plagiarisedCount"]:
        if key not in summary:
            raise ApiError(f"Сводка по заданию не содержит поле '{key}': {summary}")

    assert summary["assignmentId"] == assignment_id
    assert summary["totalWorks"] >= 2, "По assignment_id мы отправляли минимум 2 работы"
    assert summary["plagiarisedCount"] >= 1, "Должна быть хотя бы одна плагиат-работа"

    print(
        f"  OK: сводка по заданию assignmentId={assignment_id}, "
        f"totalWorks={summary['totalWorks']}, plagiarisedCount={summary['plagiarisedCount']}"
    )

    return {
        "assignment_id": assignment_id,
        "work1_id": work1_id,
        "file1_id": file1_id,
        "work2_id": work2_id,
        "file2_id": file2_id,
        "work3_id": work3_id,
        "file3_id": file3_id,
    }


def test_plagiarism_link_and_score(session: requests.Session, work1_id: str, work2_id: str) -> None:
    print("== Доп. проверка связки плагиата (sourceWorkId, plagiarismScore) ==")

    reports2 = get_work_reports(session, work2_id)
    assert reports2, "По второй работе должны быть отчёты"

    plag_reports = [r for r in reports2 if r.get("isPlagiarism")]
    assert plag_reports, "Должен быть хотя бы один отчёт с isPlagiarism = true"

    plag = plag_reports[0]
    source_id = plag.get("sourceWorkId")
    score = plag.get("plagiarismScore")

    if source_id is None:
        raise ApiError("У плагиат-отчёта не заполнен sourceWorkId")

    if source_id != work1_id:
        raise ApiError(
            f"Ожидали sourceWorkId={work1_id} (первая работа), а получили {source_id}"
        )

    if score is None or score < 100:
        raise ApiError(
            f"Ожидали plagiarismScore >= 100, получили {score!r}"
        )

    print(
        f"  OK: у плагиат-работы sourceWorkId указывает на первую работу, "
        f"plagiarismScore={score}"
    )


def test_wordcloud(session: requests.Session, file_id: str) -> None:
    print("== Проверка word cloud ==")
    url = f"{PUBLIC_API_URL}/api/files/{file_id}/wordCloud"
    r = session.get(url)
    assert_status(r, 200)

    content_type = r.headers.get("Content-Type", "")
    if "image/png" not in content_type:
        raise ApiError(
            f"Ожидали image/png от /api/files/{file_id}/wordCloud, "
            f"получили Content-Type={content_type}"
        )

    print("  OK: wordCloud вернул PNG-картинку")


def test_not_found_scenarios(session: requests.Session) -> None:
    print("== Проверка отрицательных сценариев (404) ==")

    # Работа с случайным GUID (не существует)
    random_work_id = str(uuid.uuid4())
    url_work = f"{PUBLIC_API_URL}/api/works/{random_work_id}/reports"
    r_work = session.get(url_work)
    assert_status(r_work, 404)
    print(f"  OK: /api/works/{random_work_id}/reports вернул 404 для несуществующей работы")

    # Задание, для которого не было ни одной работы
    random_assignment = f"nonexistent-{uuid.uuid4()}"
    url_assignment = f"{PUBLIC_API_URL}/api/assignments/{random_assignment}/reports"
    r_ass = session.get(url_assignment)
    assert_status(r_ass, 404)
    print(
        f"  OK: /api/assignments/{random_assignment}/reports вернул 404 "
        "для несуществующего assignmentId"
    )


def main() -> None:
    print("=== Запуск интеграционных тестов HSEHomeworkChecker ===")
    print(f"  PUBLIC_API_URL   = {PUBLIC_API_URL}")
    print(f"  CHECKER_URL      = {CHECKER_URL}")
    print(f"  FILE_STORAGE_URL = {FILE_STORAGE_URL}")
    print()

    session = requests.Session()

    try:
        test_health_checks(session)
        print()

        ids = test_submit_and_reports(session)
        print()

        test_plagiarism_link_and_score(session, ids["work1_id"], ids["work2_id"])
        print()

        test_wordcloud(session, ids["file1_id"])
        print()

        test_not_found_scenarios(session)

        print("\n=== ВСЕ ТЕСТЫ ПРОШЛИ УСПЕШНО ✅ ===")

    except ApiError as e:
        print("\n=== ТЕСТЫ УПАЛИ ❌ ===")
        print(e)
    except Exception as e:
        print("\n=== НЕОЖИДАННАЯ ОШИБКА ❌ ===")
        print(repr(e))


if __name__ == "__main__":
    main()
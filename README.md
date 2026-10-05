# TaskSpace

- [Паспорт проекта](PROJECT.md)
- [Требования безопасности](security-requirements.md)
- [Модель угроз](threat-model.md)
- [Проектные решения безопасности](security-design.md)
- [Вклад участников](CONTRIBUTIONS.md)
- [Использование генеративного ИИ](AI_USAGE.md)

API: вход, сессии, просмотр и создание задач своей команды, сдача, возврат и принятие результата, SQLite и интеграционные тесты. React не входит в текущую версию; D-05/SR-11 не заявляются проверенными.

Для запуска нужен .NET SDK 10. Из корня репозитория:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/TaskSpace.Api
```

API запускается на `http://127.0.0.1:5080`. Проверка: `curl http://127.0.0.1:5080/health` возвращает `{"status":"ok"}`. Учебные аккаунты и примеры запросов — в [инструкции запуска](PROJECT.md#10-локальный-запуск). Запросы для HTTP-клиента IDE — в [TaskSpace.http](TaskSpace.http).

## Проверки M2

```bash
dotnet test --logger "trx;LogFileName=m2.trx"
bash scripts/smoke.sh
```

Каждый тест создаёт отдельную чистую SQLite-базу и удаляет её после выполнения. Smoke сам запускает настоящий HTTP-сервер, создаёт задачу, сдачу и решение, останавливает процесс и запускает его с той же базой. Отдельный сервер и удаление рабочей базы не нужны. Требуется .NET SDK 10; первый запуск скачивает NuGet-пакеты.

[Условия, запросы и результаты](docs/M2_CHECKS.md). TRX сохраняется в `tests/TaskSpace.Api.Tests/TestResults/`. Ненулевой код завершения означает ошибку проверки.

Сдача: `POST /api/tasks/{id}/submissions` с `{"text":"Результат","expectedVersion":1}` под сессией исполнителя. Решение: `POST /api/tasks/{id}/decisions` с `{"submissionId":1,"kind":"Returned","remark":"Доработать","expectedVersion":2}` под сессией автора. Для принятия используется `kind: "Accepted"`. После `409` нужно перечитать `GET /api/tasks/{id}` и заново выбрать действие; автоматически повторять старое решение нельзя.

В ограниченном окружении macOS при зависании запуска используйте `DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test`; smoke задаёт эту переменную сам.

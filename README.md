# TaskSpace

- [Паспорт проекта](PROJECT.md)
- [Требования безопасности](security-requirements.md)
- [Модель угроз](threat-model.md)
- [Проектные решения безопасности](security-design.md)
- [Вклад участников](CONTRIBUTIONS.md)
- [Использование генеративного ИИ](AI_USAGE.md)

Серверная основа M1: вход, сессии, просмотр и создание задач своей команды, SQLite и тесты. Сдача результата, решение автора и React ещё не реализованы.

Для запуска нужен .NET SDK 10. Из корня репозитория:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/TaskSpace.Api
```

API запускается на `http://127.0.0.1:5080`. Проверка: `curl http://127.0.0.1:5080/health` возвращает `{"status":"ok"}`. Учебные аккаунты и примеры запросов — в [инструкции запуска](PROJECT.md#10-локальный-запуск). Запросы для HTTP-клиента IDE — в [TaskSpace.http](TaskSpace.http).

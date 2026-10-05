# Проверки M2

## Воспроизведение

Требуется .NET SDK 10. Команды из корня репозитория:

```bash
dotnet restore --locked-mode
dotnet test --logger "trx;LogFileName=m2.trx"
bash scripts/smoke.sh
```

В ограниченном окружении macOS: `DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --logger "trx;LogFileName=m2.trx"`. Smoke включает polling сам. Тесты используют настоящую файловую SQLite, новые базы для каждого случая. Демонстрационные фикстуры: A — alice (автор), bob (исполнитель), carol; B — dave, erin; frank состоит в обеих. Пароль учебных аккаунтов `Study123!`. Клиенты имеют независимые bearer-сессии.

## Условия и ожидаемые результаты

Команда для отдельного случая: `dotnet test --filter 'FullyQualifiedName~ИМЯ_МЕТОДА'`. Конкретные параметры теорий и фактический результат каждого случая записаны в [M2_RESULTS.md](M2_RESULTS.md) и машинном TRX. Проверка отсутствия изменений сравнивает всю базу: задачи, сдачи, решения, сессии и справочники.

| Требование / условие | Тест / HTTP-запрос | Ожидаемый результат |
| --- | --- | --- |
| SR-01: нет bearer, неизвестный или подменённый токен | `Protected_endpoints_reject_invalid_sessions_without_changes`; GET задачи/списка, POST создания/сдачи/решения | 401, общая ошибка, no-store, база неизменна |
| SR-01: токен только в cookie/query, неверная схема/несколько заголовков | `Tokens_in_query_or_cookie_and_body_userId_do_not_authenticate`, `Wrong_auth_scheme_and_multiple_authorization_values_are_rejected` | 401 |
| Login: правильный/неправильный пароль | `LoginAsync` в позитивных тестах; `Incorrect_credentials_do_not_create_sessions_or_disclose_user` | 200 и разные токены / 401 с одинаковой ошибкой без новой сессии |
| SR-03: чужая команда/задача и история | `Foreign_and_missing_teams_have_identical_responses_and_no_writes`, `Member_can_read_own_tasks_and_outsider_cannot_read_foreign_history` | чужой и отсутствующий объект дают одинаковый 404 без данных |
| SR-03, роли: прямой HTTP на сдачу/решение | `Direct_http_cannot_spoof_roles_or_cross_team`; POST `/api/tasks/1/submissions` или `/decisions` | чужая команда 404, неверная роль своей команды 403; база неизменна |
| SR-09: подмена userId, authorId, teamId, status, history | `Spoofed_author_team_status_version_and_history_are_ignored`, сквозной `Two_sessions_return_resubmit_reject_stale_decision_and_preserve_history_after_restart` | автор/исполнитель берутся из сессии, поля истории и состояния не задаются клиентом |
| SR-07: попытка изменить/удалить задачу, сдачу или решение | `History_cannot_be_edited_or_deleted`; PUT/PATCH/DELETE | 404/405; история и вся база неизменны |
| Позитивный сценарий и устаревшая вкладка | `Two_sessions_return_resubmit_reject_stale_decision_and_preserve_history_after_restart`; создать → R1 → возврат → R2 → старое принятие → принятие R2 | 201 для переходов; старый expectedVersion или submissionId даёт 409 без записи; версия 5, по две сдачи/решения |
| Одновременная сдача двух сессий bob | `Concurrent_independent_sessions_create_only_one_submission`; два POST с expectedVersion=1 | ровно один 201 и один 409, одна сдача и версия 2 |
| Неверное решение / двойное принятие / сдача после принятия | `Invalid_or_stale_decision_preserves_history`, `Two_author_sessions_cannot_decide_the_same_submission_twice` | 400 для некорректного решения; 409 для устаревшего и повторного перехода; одна запись решения |
| Ошибка после вставки сдачи | `Failure_after_submission_insert_rolls_back_history_and_state`; SQLite trigger отклоняет UPDATE | 500 с общей ошибкой, вставка сдачи и статус откатываются |
| Недопустимая сдача | `Invalid_submission_does_not_change_state`; пустой текст/старая версия | 400/409, база неизменна |
| Отсутствие утечки в ошибках | тесты доступа, login, `Invalid_json_returns_sanitized_400`, `Unexpected_database_errors_are_sanitized_in_all_environments` | только общие JSON-ошибки без текста запроса, SQL, чужой истории, токена |
| Остановка и повторный запуск настоящего процесса | `Real_server_starts_on_clean_database_and_preserves_state_after_process_restart`, `bash scripts/smoke.sh` | после kill/restart доступны созданная задача, сдача, принятие и прежняя сессия |
| Строка `<b>text</b><img src=x onerror=alert(1)>` | сквозной тест сохраняет название, описание, результат и замечание | API возвращает исходную строку как JSON, состояние меняется только разрешёнными командами |

## Граница результата

React не входит в версию. Проверка JSON round-trip не является DOM-проверкой, D-05/SR-11 не подтверждены. На работающем HTTP проверен сквозной сценарий; расширенные негативные случаи проходят через HttpClient/WebApplicationFactory с тем же ASP.NET pipeline и реальной SQLite. Интерфейс не участвует в авторизации.

Человеческая проверка новых изменений ещё не выполнена. Для передачи участнику: выполнить обе команды выше, проверить TRX и просмотреть серверные методы и тесты. Push и отправка сообщения команде не выполнялись.

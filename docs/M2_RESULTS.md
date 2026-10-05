# Фактические результаты M2

Проверка 2026-10-05: .NET SDK 10.0.401, macOS arm64, SQLite на диске.

Команда: `DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --no-restore --logger "trx;LogFileName=m2.trx"`.

Всего: 86; успешно: 86; пропущенных: 0.

Условия, HTTP-запросы и ожидания — в [M2_CHECKS.md](M2_CHECKS.md). Имена ниже включают конкретные параметры каждого случая. Passed означает, что выполнены все проверки статуса, тела и сохранённых данных, заданные тестом.

| Тест / параметры условия | Фактический результат |
| --- | --- |
| `AuthenticationTests.Health_is_public_and_returns_no_domain_data` | Passed |
| `AuthenticationTests.Incorrect_credentials_do_not_create_sessions_or_disclose_user(username: "alice", password: "wrong-demo-password")` | Passed |
| `AuthenticationTests.Incorrect_credentials_do_not_create_sessions_or_disclose_user(username: "alice' OR 1=1 --", password: "Study123!")` | Passed |
| `AuthenticationTests.Incorrect_credentials_do_not_create_sessions_or_disclose_user(username: "unknown-user", password: "Study123!")` | Passed |
| `AuthenticationTests.Missing_credentials_are_bad_requests(username: "   ", password: "Study123!")` | Passed |
| `AuthenticationTests.Missing_credentials_are_bad_requests(username: "", password: "Study123!")` | Passed |
| `AuthenticationTests.Missing_credentials_are_bad_requests(username: "alice", password: "")` | Passed |
| `AuthenticationTests.Missing_credentials_are_bad_requests(username: "alice", password: null)` | Passed |
| `AuthenticationTests.Missing_credentials_are_bad_requests(username: null, password: "Study123!")` | Passed |
| `AuthenticationTests.Passwords_are_salted_and_only_token_hashes_are_stored` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "GET", path: "/api/tasks/1", kind: "malformed")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "GET", path: "/api/tasks/1", kind: "missing")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "GET", path: "/api/tasks/1", kind: "tampered")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "GET", path: "/api/tasks/1", kind: "unknown")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "GET", path: "/api/teams/1/tasks", kind: "missing")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "GET", path: "/api/teams/1/tasks", kind: "tampered")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "GET", path: "/api/teams/1/tasks", kind: "unknown")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/tasks/1/decisions", kind: "missing")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/tasks/1/decisions", kind: "tampered")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/tasks/1/decisions", kind: "unknown")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/tasks/1/submissions", kind: "missing")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/tasks/1/submissions", kind: "tampered")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/tasks/1/submissions", kind: "unknown")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/teams/1/tasks", kind: "missing")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/teams/1/tasks", kind: "tampered")` | Passed |
| `AuthenticationTests.Protected_endpoints_reject_invalid_sessions_without_changes(method: "POST", path: "/api/teams/1/tasks", kind: "unknown")` | Passed |
| `AuthenticationTests.Responses_and_logs_do_not_expose_credentials_or_hashes` | Passed |
| `AuthenticationTests.Tokens_in_query_or_cookie_and_body_userId_do_not_authenticate` | Passed |
| `AuthenticationTests.Wrong_auth_scheme_and_multiple_authorization_values_are_rejected` | Passed |
| `PersistenceTests.Concurrent_logins_and_creations_produce_independent_valid_records` | Passed |
| `PersistenceTests.Restart_preserves_seed_history_sessions_and_created_tasks` | Passed |
| `PersistenceTests.Sqlite_enforces_relational_invariants(violation: "cross-task-submission")` | Passed |
| `PersistenceTests.Sqlite_enforces_relational_invariants(violation: "duplicate-decision")` | Passed |
| `PersistenceTests.Sqlite_enforces_relational_invariants(violation: "duplicate-session")` | Passed |
| `PersistenceTests.Sqlite_enforces_relational_invariants(violation: "other-assignee")` | Passed |
| `PersistenceTests.Sqlite_enforces_relational_invariants(violation: "other-author")` | Passed |
| `PersistenceTests.Sqlite_enforces_relational_invariants(violation: "self")` | Passed |
| `PersistenceTests.Unexpected_database_errors_are_sanitized_in_all_environments(environment: "Development")` | Passed |
| `PersistenceTests.Unexpected_database_errors_are_sanitized_in_all_environments(environment: "Production")` | Passed |
| `ServerSmokeTests.Real_server_starts_on_clean_database_and_preserves_state_after_process_restart` | Passed |
| `TaskAccessTests.Cors_allows_only_the_configured_origin(origin: "http://localhost:5173", allowed: True)` | Passed |
| `TaskAccessTests.Cors_allows_only_the_configured_origin(origin: "https://other.example", allowed: False)` | Passed |
| `TaskAccessTests.Foreign_and_missing_teams_have_identical_responses_and_no_writes` | Passed |
| `TaskAccessTests.Invalid_assignees_are_rejected_without_changes(assigneeId: -1)` | Passed |
| `TaskAccessTests.Invalid_assignees_are_rejected_without_changes(assigneeId: 0)` | Passed |
| `TaskAccessTests.Invalid_assignees_are_rejected_without_changes(assigneeId: 1)` | Passed |
| `TaskAccessTests.Invalid_assignees_are_rejected_without_changes(assigneeId: 5)` | Passed |
| `TaskAccessTests.Invalid_assignees_are_rejected_without_changes(assigneeId: 99999)` | Passed |
| `TaskAccessTests.Invalid_json_returns_sanitized_400(body: "[]")` | Passed |
| `TaskAccessTests.Invalid_json_returns_sanitized_400(body: "null")` | Passed |
| `TaskAccessTests.Invalid_json_returns_sanitized_400(body: "{")` | Passed |
| `TaskAccessTests.Invalid_json_returns_sanitized_400(body: "{\"title\":\"Task\",\"description\":\"Text\",\"ass"···)` | Passed |
| `TaskAccessTests.Invalid_text_is_rejected_without_changes(title: "  ", description: "Text")` | Passed |
| `TaskAccessTests.Invalid_text_is_rejected_without_changes(title: "", description: "Text")` | Passed |
| `TaskAccessTests.Invalid_text_is_rejected_without_changes(title: "Task", description: "  ")` | Passed |
| `TaskAccessTests.Invalid_text_is_rejected_without_changes(title: "Task", description: "")` | Passed |
| `TaskAccessTests.Invalid_text_is_rejected_without_changes(title: "Task", description: null)` | Passed |
| `TaskAccessTests.Invalid_text_is_rejected_without_changes(title: null, description: "Text")` | Passed |
| `TaskAccessTests.Member_can_read_own_tasks_and_outsider_cannot_read_foreign_history` | Passed |
| `TaskAccessTests.Ordinary_member_can_create_and_other_members_can_read_the_new_task` | Passed |
| `TaskAccessTests.Spoofed_author_team_status_version_and_history_are_ignored` | Passed |
| `TaskAccessTests.Text_length_limits_and_plain_text_round_trip_are_preserved` | Passed |
| `TaskAccessTests.User_in_two_teams_can_read_both_and_the_complete_history` | Passed |
| `WorkflowTests.Concurrent_independent_sessions_create_only_one_submission` | Passed |
| `WorkflowTests.Direct_http_cannot_spoof_roles_or_cross_team(username: "alice", action: "submissions", status: 403)` | Passed |
| `WorkflowTests.Direct_http_cannot_spoof_roles_or_cross_team(username: "bob", action: "decisions", status: 403)` | Passed |
| `WorkflowTests.Direct_http_cannot_spoof_roles_or_cross_team(username: "carol", action: "decisions", status: 403)` | Passed |
| `WorkflowTests.Direct_http_cannot_spoof_roles_or_cross_team(username: "carol", action: "submissions", status: 403)` | Passed |
| `WorkflowTests.Direct_http_cannot_spoof_roles_or_cross_team(username: "dave", action: "decisions", status: 404)` | Passed |
| `WorkflowTests.Direct_http_cannot_spoof_roles_or_cross_team(username: "dave", action: "submissions", status: 404)` | Passed |
| `WorkflowTests.Failure_after_submission_insert_rolls_back_history_and_state` | Passed |
| `WorkflowTests.History_cannot_be_edited_or_deleted(method: "DELETE", path: "/api/tasks/3/decisions/1")` | Passed |
| `WorkflowTests.History_cannot_be_edited_or_deleted(method: "DELETE", path: "/api/tasks/3/submissions/1")` | Passed |
| `WorkflowTests.History_cannot_be_edited_or_deleted(method: "PATCH", path: "/api/tasks/3/submissions/1")` | Passed |
| `WorkflowTests.History_cannot_be_edited_or_deleted(method: "PUT", path: "/api/tasks/3")` | Passed |
| `WorkflowTests.History_cannot_be_edited_or_deleted(method: "PUT", path: "/api/tasks/3/decisions/1")` | Passed |
| `WorkflowTests.Invalid_or_stale_decision_preserves_history(kind: "Accepted", remark: null, version: 3, submissionId: 2, status: 409)` | Passed |
| `WorkflowTests.Invalid_or_stale_decision_preserves_history(kind: "Accepted", remark: null, version: 4, submissionId: 1, status: 409)` | Passed |
| `WorkflowTests.Invalid_or_stale_decision_preserves_history(kind: "Deleted", remark: "remark", version: 4, submissionId: 2, status: 400)` | Passed |
| `WorkflowTests.Invalid_or_stale_decision_preserves_history(kind: "Returned", remark: "   ", version: 4, submissionId: 2, status: 400)` | Passed |
| `WorkflowTests.Invalid_or_stale_decision_preserves_history(kind: "Returned", remark: null, version: 4, submissionId: 2, status: 400)` | Passed |
| `WorkflowTests.Invalid_submission_does_not_change_state(text: "   ", version: 1, status: 400)` | Passed |
| `WorkflowTests.Invalid_submission_does_not_change_state(text: "", version: 1, status: 400)` | Passed |
| `WorkflowTests.Invalid_submission_does_not_change_state(text: "result", version: 0, status: 409)` | Passed |
| `WorkflowTests.Two_author_sessions_cannot_decide_the_same_submission_twice` | Passed |
| `WorkflowTests.Two_sessions_return_resubmit_reject_stale_decision_and_preserve_history_after_restart` | Passed |

Отдельный запуск `bash scripts/smoke.sh`: Passed, 1/1; новый процесс сервера остановлен и перезапущен, задача, сдача и решение сохранены.

Commit реализации: `c73e8c574280eeb2dd335018431b2cccc6d92a6f`.

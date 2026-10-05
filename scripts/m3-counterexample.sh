#!/usr/bin/env bash
# M3: контрпример D-04 на настоящем HTTP-сервере.
# Сервер должен быть запущен (dotnet run --project src/TaskSpace.Api); BASE задаёт адрес.
# Скрипт создаёт новую задачу alice → bob и не трогает учебные задачи 1–3.
set -euo pipefail
BASE="${BASE:-http://127.0.0.1:5080}"

login() {
  curl -s "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    --data "{\"username\":\"$1\",\"password\":\"Study123!\"}" | sed -E 's/.*"token":"([^"]+)".*/\1/'
}
# post TOKEN PATH JSON → печатает "HTTP-код тело"
post() {
  curl -s -o /tmp/m3-body -w '%{http_code}' "$BASE$2" -H "Authorization: Bearer $1" \
    -H 'Content-Type: application/json' --data "$3"
  printf ' %s\n' "$(cat /tmp/m3-body)"
}
field() { sed -E "s/.*\"$1\":([0-9]+).*/\1/"; }

ALICE=$(login alice)
BOB=$(login bob)
TASK=$(post "$ALICE" /api/teams/1/tasks '{"title":"M3 R1/R2","description":"Counterexample","assigneeId":2}' | field id)
echo "Задача $TASK создана, версия 1"

R1=$(post "$BOB" "/api/tasks/$TASK/submissions" '{"text":"R1","expectedVersion":1}' | field id)
echo "1. Исполнитель сдал R1=$R1; автор читает её при версии 2"
echo "2. Автор возвращает R1 (версия 2 → 3): $(post "$ALICE" "/api/tasks/$TASK/decisions" "{\"submissionId\":$R1,\"kind\":\"Returned\",\"remark\":\"Fix it\",\"expectedVersion\":2}" | cut -c1-3)"
R2=$(post "$BOB" "/api/tasks/$TASK/submissions" '{"text":"R2","expectedVersion":3}' | field id)
echo "3. Исполнитель сдал R2=$R2 (версия 3 → 4)"
echo "4. Старая вкладка: принять R1 с версией 2 → $(post "$ALICE" "/api/tasks/$TASK/decisions" "{\"submissionId\":$R1,\"kind\":\"Accepted\",\"expectedVersion\":2}")"
echo "Текущее состояние:"
curl -s "$BASE/api/tasks/$TASK" -H "Authorization: Bearer $ALICE" \
  | sed -E 's/.*("status":"[A-Za-z]+","version":[0-9]+,"currentSubmissionId":[0-9]+).*/  \1/'
echo
echo "5. Новое решение по R2 с версией 4 → $(post "$ALICE" "/api/tasks/$TASK/decisions" "{\"submissionId\":$R2,\"kind\":\"Accepted\",\"expectedVersion\":4}" | cut -c1-3)"

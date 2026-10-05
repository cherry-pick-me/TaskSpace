import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, afterEach, expect, test, vi } from "vitest";
import App from "./App.jsx";

const payload = "<b>text</b><img src=x onerror=alert(1)>";
let current, writes, fetchMock, userId, conflict, deny;
function response(data, status = 200) {
  return { ok: status < 400, status, json: async () => structuredClone(data) };
}
beforeEach(() => {
  userId = 1;
  conflict = false;
  deny = false;
  writes = [];
  current = {
    id: 1,
    teamId: 1,
    authorId: 1,
    assigneeId: 2,
    title: payload,
    description: payload,
    status: "InReview",
    version: 2,
    currentSubmissionId: 10,
    submissions: [
      {
        id: 10,
        submitterId: 2,
        text: payload,
        createdAt: "2026-10-05T10:00:00Z",
      },
    ],
    decisions: [
      {
        id: 9,
        submissionId: 10,
        authorId: 1,
        kind: "Returned",
        remark: payload,
        createdAt: "2026-10-05T10:00:00Z",
      },
    ],
  };
  fetchMock = vi.fn(async (url, options) => {
    const path = new URL(url).pathname;
    if (path === "/api/auth/login")
      return response({
        token: "memory-only-token",
        user: { id: userId, displayName: "Алиса" },
        teams: [{ id: 1, name: "Команда A" }],
      });
    if (deny) return response({ error: "Требуется действующая сессия." }, 401);
    if (options.method === "POST") {
      const body = JSON.parse(options.body);
      writes.push({ path, body });
      if (conflict) {
        current = {
          ...current,
          version: 4,
          currentSubmissionId: 11,
          submissions: [
            ...current.submissions,
            {
              id: 11,
              submitterId: 2,
              text: "R2",
              createdAt: "2026-10-05T11:00:00Z",
            },
          ],
        };
        return response({ error: "Конфликт" }, 409);
      }
      if (path.endsWith("/submissions")) {
        current = {
          ...current,
          status: "InReview",
          version: current.version + 1,
          currentSubmissionId: 11,
          submissions: [
            ...current.submissions,
            {
              id: 11,
              submitterId: 2,
              text: body.text,
              createdAt: "2026-10-05T11:00:00Z",
            },
          ],
        };
      } else
        current = {
          ...current,
          status: body.kind === "Accepted" ? "Accepted" : "ChangesRequested",
          version: current.version + 1,
        };
      return response({}, 201);
    }
    return response(path.endsWith("/tasks") ? [current] : current);
  });
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("localStorage", window.localStorage);
  vi.stubGlobal("sessionStorage", window.sessionStorage);
  localStorage.clear();
  sessionStorage.clear();
});
afterEach(() => vi.unstubAllGlobals());
async function enter(user = userEvent.setup()) {
  await user.type(screen.getByLabelText("Логин"), "alice");
  await user.type(screen.getByLabelText("Пароль"), "Study123!");
  await user.click(screen.getByRole("button", { name: "Войти" }));
  await user.click(
    await screen.findByRole("button", {
      name: (name) => name.includes(payload),
    }),
  );
  await screen.findByRole("heading", { name: payload });
  return user;
}
test("SR-11: untrusted title, description, submission and remark are text, with no executable elements or API writes", async () => {
  const initial = structuredClone(current);
  const { container } = render(<App />);
  await enter();
  expect(screen.getAllByText(payload).length).toBe(5);
  expect(container.querySelector("b, img, script")).toBeNull();
  expect(writes).toEqual([]);
  expect(current).toEqual(initial);
});
test("bearer stays in memory and disappears on logout and fresh mount", async () => {
  const view = render(<App />);
  const user = await enter();
  const reads = fetchMock.mock.calls.filter(([url]) => !url.endsWith("/login"));
  for (const [, options] of reads) {
    expect(options.headers.Authorization).toBe("Bearer memory-only-token");
    expect(options.credentials).toBe("omit");
  }
  expect(localStorage.length).toBe(0);
  expect(sessionStorage.length).toBe(0);
  expect(document.cookie).toBe("");
  view.unmount();
  const fresh = render(<App />);
  expect(screen.getByRole("button", { name: "Войти" })).toBeTruthy();
  await enter(user);
  await user.click(screen.getByRole("button", { name: "Выйти" }));
  expect(screen.getByRole("button", { name: "Войти" })).toBeTruthy();
  fresh.unmount();
});
test("409 rereads task and requires a new click; stale acceptance is never automatically repeated", async () => {
  render(<App />);
  const user = await enter();
  conflict = true;
  await user.click(screen.getByRole("button", { name: "Принять" }));
  expect(await screen.findByRole("status")).toHaveProperty(
    "textContent",
    expect.stringContaining("повторите действие вручную"),
  );
  expect(writes).toHaveLength(1);
  expect(writes[0].body).toMatchObject({
    submissionId: 10,
    expectedVersion: 2,
    kind: "Accepted",
  });
  expect(
    screen.getByRole("heading", { name: "Решение по сдаче #11" }),
  ).toBeTruthy();
  conflict = false;
  await user.click(screen.getByRole("button", { name: "Принять" }));
  await waitFor(() => expect(writes).toHaveLength(2));
  expect(writes[1].body).toMatchObject({
    submissionId: 11,
    expectedVersion: 4,
  });
  await screen.findByText("Действие сохранено.");
});
test("executor sends text and read version, then sees the updated history", async () => {
  userId = 2;
  current = {
    ...current,
    status: "Assigned",
    version: 1,
    currentSubmissionId: null,
    submissions: [],
    decisions: [],
  };
  render(<App />);
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("Логин"), "bob");
  await user.type(screen.getByLabelText("Пароль"), "Study123!");
  await user.click(screen.getByRole("button", { name: "Войти" }));
  await user.click(
    await screen.findByRole("button", {
      name: (name) => name.includes(payload),
    }),
  );
  await user.type(await screen.findByLabelText("Результат"), "Готово");
  await user.click(screen.getByRole("button", { name: "Отправить результат" }));
  await screen.findByText("Действие сохранено.");
  expect(writes[0]).toEqual({
    path: "/api/tasks/1/submissions",
    body: { text: "Готово", expectedVersion: 1 },
  });
  expect(screen.getByText("Готово")).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Принять" })).toBeNull();
});
test("author must explicitly enter a return remark", async () => {
  render(<App />);
  const user = await enter();
  expect(
    screen.getByRole("button", { name: "Вернуть на доработку" }).disabled,
  ).toBe(true);
  await user.type(screen.getByLabelText("Замечание"), "Исправить");
  await user.click(
    screen.getByRole("button", { name: "Вернуть на доработку" }),
  );
  await screen.findByText("Действие сохранено.");
  expect(writes[0].body).toEqual({
    submissionId: 10,
    kind: "Returned",
    remark: "Исправить",
    expectedVersion: 2,
  });
});
test("invalid session clears the in-memory login and displays the server error", async () => {
  render(<App />);
  const user = await enter();
  deny = true;
  await user.click(screen.getByRole("button", { name: "Принять" }));
  await screen.findByRole("button", { name: "Войти" });
  expect(screen.getByRole("alert").textContent).toBe(
    "Требуется действующая сессия.",
  );
});

import React, { useEffect, useState } from "react";
const labels = {
  Assigned: "Назначена",
  InReview: "На проверке",
  ChangesRequested: "На доработке",
  Accepted: "Принята",
};
const base = import.meta.env.VITE_API_URL || "http://127.0.0.1:5080";
export default function App() {
  const [session, setSession] = useState(null),
    [team, setTeam] = useState(""),
    [tasks, setTasks] = useState([]),
    [task, setTask] = useState(null);
  const [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [busy, setBusy] = useState(false),
    [draft, setDraft] = useState(""),
    [remark, setRemark] = useState("");
  function logout() {
    setSession(null);
    setTeam("");
    setTasks([]);
    setTask(null);
    setDraft("");
    setRemark("");
    setNotice("");
    setError("");
  }
  async function request(
    path,
    { token = session?.token, body, method = "GET", signal } = {},
  ) {
    const headers = {};
    if (token) headers.Authorization = `Bearer ${token}`;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    const r = await fetch(base + path, {
      method,
      headers,
      signal,
      credentials: "omit",
      ...(body !== undefined && { body: JSON.stringify(body) }),
    });
    const data = await r.json();
    if (!r.ok) {
      if (r.status === 401 && token) logout();
      throw Object.assign(new Error(data.error || "Запрос отклонён."), {
        status: r.status,
      });
    }
    return data;
  }
  useEffect(() => {
    if (!session || !team) return;
    const controller = new AbortController();
    setTask(null);
    setTasks([]);
    setDraft("");
    setRemark("");
    setError("");
    setNotice("");
    request(`/api/teams/${team}/tasks`, { signal: controller.signal })
      .then(setTasks)
      .catch((e) => {
        if (e.name !== "AbortError") setError(e.message);
      });
    return () => controller.abort();
  }, [session, team]);
  async function login(e) {
    e.preventDefault();
    const form = new FormData(e.currentTarget);
    setBusy(true);
    setError("");
    try {
      const data = await request("/api/auth/login", {
        method: "POST",
        token: null,
        body: {
          username: form.get("username"),
          password: form.get("password"),
        },
      });
      setSession(data);
      setTeam(String(data.teams[0]?.id || ""));
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  }
  async function open(id) {
    setBusy(true);
    setError("");
    setNotice("");
    setDraft("");
    setRemark("");
    try {
      setTask(await request(`/api/tasks/${id}`));
    } catch (e) {
      setTask(null);
      setError(e.message);
    } finally {
      setBusy(false);
    }
  }
  async function act(action, body) {
    setBusy(true);
    setError("");
    setNotice("");
    const id = task.id;
    try {
      await request(`/api/tasks/${id}/${action}`, {
        method: "POST",
        body: { ...body, expectedVersion: task.version },
      });
      setTask(await request(`/api/tasks/${id}`));
      setTasks(await request(`/api/teams/${team}/tasks`));
      setDraft("");
      setRemark("");
      setNotice("Действие сохранено.");
    } catch (e) {
      if (e.status === 409) {
        try {
          setTask(await request(`/api/tasks/${id}`));
          setNotice(
            "Задача изменилась. Данные обновлены. Проверьте текущую сдачу и повторите действие вручную.",
          );
        } catch (e) {
          setTask(null);
          setError(e.message);
        }
      } else setError(e.message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <main>
      <header>
        <div>
          <p className="eyebrow">КОМАНДНАЯ РАБОТА</p>
          <h1>TaskSpace</h1>
        </div>
        {session && (
          <button disabled={busy} onClick={logout}>
            Выйти
          </button>
        )}
      </header>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {notice && (
        <p role="status" className="notice">
          {notice}
        </p>
      )}
      {!session ? (
        <section className="login">
          <h2>Вход в команду</h2>
          <p>Посмотрите задачи и отправьте результат.</p>
          <form onSubmit={login}>
            <label>
              Логин
              <input
                name="username"
                autoComplete="username"
                required
                maxLength={128}
              />
            </label>
            <label>
              Пароль
              <input
                name="password"
                type="password"
                autoComplete="current-password"
                required
                maxLength={1024}
              />
            </label>
            <button className="primary" disabled={busy}>
              Войти
            </button>
          </form>
        </section>
      ) : (
        <>
          <div className="toolbar">
            <p>{session.user.displayName}</p>
            <label>
              Команда
              <select
                disabled={busy}
                value={team}
                onChange={(e) => setTeam(e.target.value)}
              >
                {session.teams.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <div className="workspace">
            <aside>
              <h2>Задачи</h2>
              {tasks.length === 0 && <p>В этой команде пока нет задач.</p>}
              {tasks.map((t) => (
                <button
                  className={`task-link ${task?.id === t.id ? "selected" : ""}`}
                  key={t.id}
                  disabled={busy}
                  onClick={() => open(t.id)}
                >
                  <strong>{t.title}</strong>
                  <span>{labels[t.status]}</span>
                </button>
              ))}
            </aside>
            <section>
              {!task ? (
                <p>Выберите задачу из списка.</p>
              ) : (
                <>
                  <p className="eyebrow">
                    ЗАДАЧА #{task.id} · ВЕРСИЯ {task.version}
                  </p>
                  <h2>{task.title}</h2>
                  <span className="badge">{labels[task.status]}</span>
                  <p className="text">{task.description}</p>
                  <p>
                    Автор #{task.authorId} · Исполнитель #{task.assigneeId}
                  </p>
                  <h3>История</h3>
                  {task.submissions.length === 0 && <p>Сдач пока нет.</p>}
                  {task.submissions.map((s) => (
                    <article key={s.id}>
                      <h4>
                        Сдача #{s.id} · Исполнитель #{s.submitterId}
                      </h4>
                      <p className="text">{s.text}</p>
                      <time>
                        {new Date(s.createdAt).toLocaleString("ru-RU")}
                      </time>
                      {task.decisions
                        .filter((d) => d.submissionId === s.id)
                        .map((d) => (
                          <div className="decision" key={d.id}>
                            <strong>
                              {d.kind === "Accepted"
                                ? "Принято"
                                : "Возвращено на доработку"}
                            </strong>
                            <p className="text">{d.remark}</p>
                            <small>Автор #{d.authorId}</small>
                          </div>
                        ))}
                    </article>
                  ))}
                  {task.assigneeId === session.user.id &&
                    ["Assigned", "ChangesRequested"].includes(task.status) && (
                      <form
                        onSubmit={(e) => {
                          e.preventDefault();
                          act("submissions", { text: draft });
                        }}
                      >
                        <h3>Сдать результат</h3>
                        <label>
                          Результат
                          <textarea
                            value={draft}
                            onChange={(e) => setDraft(e.target.value)}
                            required
                            maxLength={10000}
                          />
                        </label>
                        <button
                          className="primary"
                          disabled={busy || !draft.trim()}
                        >
                          Отправить результат
                        </button>
                      </form>
                    )}
                  {task.authorId === session.user.id &&
                    task.status === "InReview" && (
                      <form
                        onSubmit={(e) => {
                          e.preventDefault();
                          act("decisions", {
                            submissionId: task.currentSubmissionId,
                            kind: "Returned",
                            remark,
                          });
                        }}
                      >
                        <h3>Решение по сдаче #{task.currentSubmissionId}</h3>
                        <label>
                          Замечание
                          <textarea
                            value={remark}
                            onChange={(e) => setRemark(e.target.value)}
                            maxLength={10000}
                          />
                        </label>
                        <div className="actions">
                          <button
                            type="button"
                            className="primary"
                            disabled={busy}
                            onClick={() =>
                              act("decisions", {
                                submissionId: task.currentSubmissionId,
                                kind: "Accepted",
                                remark,
                              })
                            }
                          >
                            Принять
                          </button>
                          <button disabled={busy || !remark.trim()}>
                            Вернуть на доработку
                          </button>
                        </div>
                      </form>
                    )}
                </>
              )}
            </section>
          </div>
        </>
      )}
    </main>
  );
}

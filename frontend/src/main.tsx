import React, { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { macros as demoMacros, categories } from "./catalog";
import { Icon } from "./Icon";
import { readSetting, writeSetting } from "./storage";
import "./styles.css";
import { host, subscribe } from "./bridge";

type Theme = "light" | "dark";
type Notice = { title: string; body: string };
const isTheme = (v: unknown): v is Theme => v === "light" || v === "dark";
const isString = (v: unknown): v is string => typeof v === "string";
const isStrings = (v: unknown): v is string[] =>
  Array.isArray(v) && v.every(isString);

function App() {
  const [macros, setMacros] = useState(host ? [] as typeof demoMacros : demoMacros);
  const [running, setRunning] = useState<string | null>(null);
  const runningRef = useRef<string | null>(null);
  useEffect(() => subscribe((data) => {
    if (data.type === "catalog" && Array.isArray(data.macros)) setMacros(data.macros);
    if (data.type === "result") {
      runningRef.current = null;
      setRunning(null);
      setNotice({ title: data.ok ? "Запуск завершён" : "Не удалось выполнить", body: data.message || "Дополнение не вернуло описание результата." });
    }
  }), []);
  function run(id: string, name: string) {
    if (!host) {
      setNotice({ title: name, body: "Реальный запуск макросов доступен только внутри SolidWorks. Сейчас открыт демонстрационный режим — макрос не запускался." });
      return;
    }
    if (runningRef.current) return;
    runningRef.current = id;
    setRunning(id);
    try { host.postMessage({ type: "run", id }); }
    catch { runningRef.current = null; setRunning(null); setNotice({ title: "Ошибка связи", body: "Не удалось передать команду дополнению. Перезапустите панель." }); }
  }
  const [theme, setTheme] = useState(() =>
    readSetting("theme", "light" as Theme, isTheme),
  );
  const [favorites, setFavorites] = useState(() =>
    readSetting("favorites", [] as string[], isStrings),
  );
  const [server, setServer] = useState(() =>
    readSetting("server", "", isString),
  );
  const [tab, setTab] = useState<"all" | "favorites">("all");
  const [category, setCategory] = useState("Все категории");
  const [query, setQuery] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [settings, setSettings] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [storageError, setStorageError] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  const search = useRef<HTMLInputElement>(null);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);
  useEffect(() => {
    if (notice) dialog.current?.showModal();
    else dialog.current?.close();
  }, [notice]);

  function persist(key: string, value: unknown) {
    setStorageError(!writeSetting(key, value));
  }
  function changeTheme(value: Theme) {
    setTheme(value);
    persist("theme", value);
  }
  function toggleFavorite(id: string) {
    const next = favorites.includes(id)
      ? favorites.filter((x) => x !== id)
      : [...favorites, id];
    setFavorites(next);
    persist("favorites", next);
  }
  const favoriteCount = macros.filter((m) => favorites.includes(m.id)).length;
  const visible = macros.filter(
    (m) =>
      (tab === "all" || favorites.includes(m.id)) &&
      (category === "Все категории" || m.category === category) &&
      m.name
        .toLocaleLowerCase("ru")
        .includes(query.trim().toLocaleLowerCase("ru")),
  );

  return (
    <div className="workspace">
      <div className="preview-label">
        <span className="preview-dot" />
        SOLIDWORKS 2025 <span>/</span> {host ? "TASK PANE" : "TASK PANE PREVIEW"}
      </div>
      <main className="pane">
        <header className="header">
          <div className="brand">
            <span className="brand-mark">
              <Icon name="cube" size={25} />
            </span>
            <div>
              <h1>SW Tools Hub</h1>
              <span className="brand-subtitle">Всё нужное. Под рукой.</span>
            </div>
          </div>
          <button
            className="icon-button"
            aria-label={
              theme === "light"
                ? "Включить тёмную тему"
                : "Включить светлую тему"
            }
            title="Переключить тему"
            onClick={() => changeTheme(theme === "light" ? "dark" : "light")}
          >
            <Icon name={theme === "light" ? "moon" : "sun"} />
          </button>
        </header>

        {storageError && (
          <p className="storage-error" role="alert">
            Браузер запретил сохранение настроек. Изменения действуют до
            перезагрузки.
          </p>
        )}

        {settings ? (
          <section className="settings content">
            <div className="section-heading">
              <h2>Настройки</h2>
              <button
                className="icon-button"
                aria-label="Закрыть настройки"
                onClick={() => setSettings(false)}
              >
                <Icon name="close" />
              </button>
            </div>
            <p className="section-description">Ваше рабочее пространство.</p>
            <label className="field-label" htmlFor="server">
              Серверная библиотека
            </label>
            <div className="path-field">
              <Icon name="folder" size={18} />
              <input
                id="server"
                value={server}
                placeholder="\\SERVER\SolidWorks\SWToolsHub"
                onChange={(e) => {
                  setServer(e.target.value);
                  persist("server", e.target.value);
                }}
              />
            </div>
            <p className="field-hint">
              Путь сохранится на этом устройстве. Подключение к сетевой папке
              появится на этапе интеграции.
            </p>
            <span className="field-label" id="theme-label">
              Оформление
            </span>
            <div
              className="theme-picker"
              role="group"
              aria-labelledby="theme-label"
            >
              {(["light", "dark"] as const).map((t) => (
                <button
                  key={t}
                  aria-pressed={theme === t}
                  onClick={() => changeTheme(t)}
                >
                  <Icon name={t === "light" ? "sun" : "moon"} size={18} />
                  {t === "light" ? "Светлая" : "Тёмная"}
                  {theme === t && <Icon name="check" size={16} />}
                </button>
              ))}
            </div>
            <button
              className="secondary-button refresh-button"
              onClick={() =>
                setNotice({
                  title: host ? "Локальная библиотека" : "Демокаталог актуален",
                  body: "Каталог загружается при открытии панели. Сетевая библиотека будет доступна на следующем этапе.",
                })
              }
            >
              <Icon name="refresh" size={16} />
              Обновить список макросов
            </button>
            <div className="version-info">
              <span>SW Tools Hub</span>
              <code>v0.1.0</code>
              <p>{host ? "Дополнение SolidWorks · локальный запуск" : "Предварительная версия · деморежим"}</p>
            </div>
          </section>
        ) : (
          <>
            <nav className="tabs" aria-label="Библиотека">
              <button
                aria-current={tab === "all" ? "page" : undefined}
                onClick={() => setTab("all")}
              >
                <Icon name="grid" size={17} />
                Все инструменты<span className="count">{macros.length}</span>
              </button>
              <button
                aria-current={tab === "favorites" ? "page" : undefined}
                onClick={() => setTab("favorites")}
              >
                <Icon name="star" size={17} />
                Избранное<span className="count">{favoriteCount}</span>
              </button>
            </nav>
            <section className="content library">
              <div className="library-heading">
                <div>
                  <p className="eyebrow">МЕНЬШЕ РУТИНЫ. БОЛЬШЕ ТОЧНОСТИ.</p>
                  <h2>
                    {tab === "all" ? "Библиотека макросов" : "Ваше избранное"}
                  </h2>
                </div>
                <span className="library-symbol">
                  <Icon name="cube" size={32} />
                </span>
              </div>
              <div className="search-field">
                <Icon name="search" size={18} />
                <input
                  ref={search}
                  aria-label="Поиск макросов"
                  placeholder="Найти инструмент…"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                />
                {query && (
                  <button
                    className="clear-search"
                    aria-label="Очистить поиск"
                    onClick={() => {
                      setQuery("");
                      search.current?.focus();
                    }}
                  >
                    <Icon name="close" size={15} />
                  </button>
                )}
              </div>
              <div className="category-field">
                <label htmlFor="category">Категория</label>
                <select
                  id="category"
                  value={category}
                  onChange={(e) => setCategory(e.target.value)}
                >
                  <option>Все категории</option>
                  {categories.map((c) => (
                    <option key={c}>{c}</option>
                  ))}
                </select>
              </div>
              <div className="list-heading">
                <span>
                  {tab === "all" ? "ИНСТРУМЕНТЫ" : "ЗАКРЕПЛЁННЫЕ"}{" "}
                  <span className="result-count">
                    {String(visible.length).padStart(2, "0")}
                  </span>
                </span>
                <span>VBA МАКРОСЫ</span>
              </div>
              <div className="macro-list">
                {visible.map((m) => (
                  <article
                    className={`macro-row ${expanded === m.id ? "expanded" : ""}`}
                    key={m.id}
                  >
                    <div className="macro-top">
                      <div className={`macro-icon ${m.icon}`}>
                        <Icon name={m.icon} size={29} />
                      </div>
                      <button
                        className="macro-summary"
                        aria-expanded={expanded === m.id}
                        aria-controls={`details-${m.id}`}
                        onClick={() =>
                          setExpanded(expanded === m.id ? null : m.id)
                        }
                      >
                        <span className="macro-title">
                          {m.name}
                          <Icon name="chevron" size={14} />
                        </span>
                        <span className="macro-description">
                          {m.description}
                        </span>
                      </button>
                      <button
                        className={`icon-button favorite ${favorites.includes(m.id) ? "is-favorite" : ""}`}
                        aria-label={`${favorites.includes(m.id) ? "Убрать из избранного" : "В избранное"}: ${m.name}`}
                        aria-pressed={favorites.includes(m.id)}
                        onClick={() => toggleFavorite(m.id)}
                      >
                        <Icon name="star" size={18} />
                      </button>
                    </div>
                    <div className="macro-bottom">
                      <span className="category-tag">{m.category}</span>
                      <button
                        className="run-button"
                        disabled={running !== null}
                        onClick={() => run(m.id, m.name)}
                      >
                        <Icon name="play" size={13} />
                        {running === m.id ? "Выполняется…" : "Запустить"}
                      </button>
                    </div>
                    <div
                      id={`details-${m.id}`}
                      className="macro-details"
                      hidden={expanded !== m.id}
                    >
                      <p>{m.details}</p>
                      <dl>
                        <dt>Документ</dt>
                        <dd>Чертёж</dd>
                        <dt>Файл</dt>
                        <dd>{m.file}</dd>
                        <dt>Точка входа</dt>
                        <dd>
                          <code>
                            {m.module}.{m.procedure}
                          </code>
                        </dd>
                      </dl>
                    </div>
                  </article>
                ))}
              </div>
              {visible.length === 0 && (
                <div className="empty-state">
                  <Icon
                    name={
                      tab === "favorites" && !favoriteCount ? "star" : "search"
                    }
                    size={30}
                  />
                  <h3>
                    {tab === "favorites" && !favoriteCount
                      ? "Важное — рядом"
                      : "Ничего не найдено"}
                  </h3>
                  <p>
                    {tab === "favorites" && !favoriteCount
                      ? "Нажмите на звезду у инструмента, чтобы добавить его сюда."
                      : "Попробуйте другое название или выберите все категории."}
                  </p>
                  <button
                    className="secondary-button"
                    onClick={() => {
                      setQuery("");
                      setCategory("Все категории");
                      if (tab === "favorites" && !favoriteCount) setTab("all");
                    }}
                  >
                    Все инструменты
                  </button>
                </div>
              )}
              {visible.length > 0 && (
                <p className="list-hint">
                  <Icon name="info" size={14} />
                  Нажмите на название, чтобы узнать больше
                </p>
              )}
            </section>
            <div className="pane-space" aria-hidden="true">
              <div className="engineering-grid">
                <Icon name="cube" size={64} />
              </div>
              <span>СОЗДАНО ДЛЯ ИНЖЕНЕРОВ</span>
            </div>
          </>
        )}
        <footer className="footer">
          <div className="demo-notice">
            <span className="demo-dot" />
            <div>
              <strong>{host ? "Локальная библиотека" : "Демонстрационный режим"}</strong>
              <span>{host ? "Запуск в текущем документе SolidWorks" : "Запуск макросов — внутри SolidWorks"}</span>
            </div>
          </div>
          <div className="footer-bottom">
            <span className="version">
              v0.1.0 <span>·</span> SOLIDWORKS 2025
            </span>
            <button
              className={settings ? "settings-link active" : "settings-link"}
              aria-pressed={settings}
              onClick={() => setSettings(!settings)}
            >
              <Icon name="settings" size={15} />
              {settings ? "К библиотеке" : "Настройки"}
            </button>
          </div>
        </footer>
      </main>
      <p className="preview-caption">
        Меньше переключений. Больше времени на проектирование.
      </p>
      <dialog
        ref={dialog}
        onCancel={() => setNotice(null)}
        onClose={() => setNotice(null)}
        aria-labelledby="notice-title"
        aria-describedby="notice-body"
      >
        <div className="dialog-icon">
          <Icon name="info" size={24} />
        </div>
        <h2 id="notice-title">{notice?.title}</h2>
        <p id="notice-body">{notice?.body}</p>
        <button
          className="primary-button"
          autoFocus
          onClick={() => setNotice(null)}
        >
          Понятно
        </button>
      </dialog>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);

import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
});

test("поиск и категории вместе, очистка и пустой результат", async ({
  page,
}) => {
  await expect(page.locator(".macro-row")).toHaveCount(2);
  await page.getByRole("textbox", { name: "Поиск макросов" }).fill("AUTOSIZE");
  await expect(page.locator(".macro-row")).toHaveCount(1);
  await page
    .getByLabel("Категория", { exact: true })
    .selectOption("Сварные конструкции");
  await expect(page.getByText("Ничего не найдено")).toBeVisible();
  await page.getByRole("button", { name: "Очистить поиск" }).click();
  await expect(page.locator(".macro-title")).toHaveText("Weldment Drilling");
  await page.getByLabel("Категория", { exact: true }).selectOption("Детали");
  await expect(page.getByText("Ничего не найдено")).toBeVisible();
  await page
    .getByRole("button", { name: "Все инструменты", exact: true })
    .click();
  await expect(page.locator(".macro-row")).toHaveCount(2);
});

test("избранное сохраняется, удаление даёт пустое состояние", async ({
  page,
}) => {
  await page
    .getByRole("button", { name: "В избранное: AutoSize", exact: true })
    .click();
  await page.reload();
  await page
    .getByRole("navigation", { name: "Библиотека" })
    .getByRole("button", { name: "Избранное" })
    .click();
  await expect(page.locator(".macro-row")).toHaveCount(1);
  await expect(page.locator(".macro-title")).toHaveText("AutoSize");
  await page
    .getByRole("button", { name: "Убрать из избранного: AutoSize" })
    .click();
  await expect(page.getByText("Важное — рядом")).toBeVisible();
});

test("темы и серверный путь переживают перезагрузку", async ({ page }) => {
  await page.getByRole("button", { name: "Включить тёмную тему" }).click();
  await page.getByRole("button", { name: "Настройки", exact: true }).click();
  const path = String.raw`\\SERVER\SolidWorks\SWToolsHub`;
  await page.getByLabel("Серверная библиотека").fill(path);
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.getByRole("button", { name: "Настройки", exact: true }).click();
  await expect(page.getByLabel("Серверная библиотека")).toHaveValue(path);
  await page.getByRole("button", { name: "Светлая", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  await page.getByRole("button", { name: "Обновить список макросов" }).click();
  await expect(page.getByRole("dialog")).toContainText("Демокаталог актуален");
});

test("описания и оба демозапуска, закрытие клавишей Escape", async ({
  page,
}) => {
  for (const name of ["AutoSize", "Weldment Drilling"]) {
    const row = page.locator(".macro-row").filter({ hasText: name });
    await row
      .getByRole("button", { name: new RegExp(name) })
      .first()
      .click();
    await expect(row.locator(".macro-details")).toBeVisible();
    await row.getByRole("button", { name: "Запустить" }).click();
    await expect(page.getByRole("dialog")).toContainText(
      "макрос не запускался",
    );
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).not.toBeVisible();
  }
});

test("узкая панель без горизонтальной прокрутки", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await expect(
    page.getByRole("heading", { name: "SW Tools Hub" }),
  ).toBeVisible();
  await page.locator(".macro-summary").last().click();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  await page.getByRole("button", { name: "Настройки", exact: true }).click();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
});

test("повреждённые локальные настройки не ломают интерфейс", async ({
  page,
}) => {
  await page.evaluate(() => {
    localStorage.setItem("swtools.favorites", "{broken");
    localStorage.setItem("swtools.theme", "123");
  });
  await page.reload();
  await expect(page.locator(".macro-row")).toHaveCount(2);
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
});

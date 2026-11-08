import { test, expect } from '@playwright/test';

test('дополнение: каталог хоста, только id, блокировка и результат ошибки', async ({ page }) => {
  await page.addInitScript(() => {
    const listeners = new Set<(event: MessageEvent) => void>();
    Object.defineProperty(window, 'chrome', { value: { webview: {
      addEventListener: (_: string, fn: (event: MessageEvent) => void) => listeners.add(fn),
      removeEventListener: (_: string, fn: (event: MessageEvent) => void) => listeners.delete(fn),
      postMessage: (message: { type: string; id?: string }) => {
        if (message.type === 'ready') {
          for (const fn of listeners) fn({ data: { type: 'catalog', macros: [{
            id: 'local-tool', name: 'Локальный инструмент', category: 'Чертежи',
            description: 'Из каталога дополнения', details: '', file: 'Local.swp',
            module: 'Module', procedure: 'main', requiredDocument: 'drawing', icon: 'cube',
          }] } } as MessageEvent);
        } else {
          localStorage.setItem('command', JSON.stringify(message));
          setTimeout(() => {
            for (const fn of listeners) fn({ data: { type: 'result', id: message.id, ok: false, message: 'Файл макроса не найден.' } } as MessageEvent);
          }, 500);
        }
      },
    } }, configurable: true });
  });
  await page.goto('/');
  await expect(page.locator('.macro-title')).toHaveText('Локальный инструмент');
  await expect(page.getByText('Локальная библиотека', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Запустить', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Выполняется…' })).toBeDisabled();
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem('command')!))).toEqual({ type: 'run', id: 'local-tool' });
  await expect(page.getByRole('dialog')).toContainText('Файл макроса не найден.');
  await page.getByRole('button', { name: 'Понятно' }).click();
  await expect(page.getByRole('button', { name: 'Запустить', exact: true })).toBeEnabled();
});

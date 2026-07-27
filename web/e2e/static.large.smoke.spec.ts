import { expect, test } from '@playwright/test';

test('large static snapshot search stays responsive and limited', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', (request) => requests.push(request.url()));

  await page.goto('/#/tasks');
  const taskSearch = page.getByRole('combobox', { name: 'Search tasks' });
  const mobileTaskSearch = page.getByRole('button', { name: 'Search tasks' });
  if (await mobileTaskSearch.isVisible()) await mobileTaskSearch.click();
  const taskStartedAt = Date.now();
  await taskSearch.fill('Large fixture task');
  await expect(page.getByRole('option')).toHaveCount(20);
  expect(Date.now() - taskStartedAt).toBeLessThan(2_000);
  await taskSearch.press('Escape');

  await page.getByRole('link', { name: 'Wiki' }).click();
  const wikiSearch = page.getByRole('combobox', { name: 'Search wiki' });
  const mobileWikiSearch = page.getByRole('button', { name: 'Search wiki' });
  if (await mobileWikiSearch.isVisible()) await mobileWikiSearch.click();
  const wikiStartedAt = Date.now();
  await wikiSearch.fill('Local fixture content');
  await expect(page.getByRole('option')).toHaveCount(20);
  expect(Date.now() - wikiStartedAt).toBeLessThan(2_000);

  const parsed = requests.map((url) => new URL(url));
  expect(parsed.some((url) => url.pathname.startsWith('/api/'))).toBe(false);
  expect(parsed.every((url) => url.hostname === '127.0.0.1' || url.hostname === 'localhost')).toBe(
    true,
  );
});

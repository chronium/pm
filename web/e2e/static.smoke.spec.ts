import { expect, test } from '@playwright/test';

test('static snapshot supports filters, task views, dependencies, wiki folders, and hash reloads', async ({
  page,
}, testInfo) => {
  const requests: string[] = [];
  page.on('request', (request) => requests.push(request.url()));

  await page.goto('/#/tasks?track=OPS');
  await expect(page.getByText('Read-only snapshot')).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Search tasks' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'New task' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Settings' })).toHaveCount(0);
  await expect(page.locator('[pmTaskRow]')).toHaveCount(2);

  await page.getByRole('link', { name: /E2E-0003/ }).click();
  await expect(page).toHaveURL(
    testInfo.project.name.includes('mobile')
      ? /#\/tasks\/E2E-0003\?track=OPS$/
      : /#\/tasks\/dialog\/E2E-0003\?track=OPS$/,
  );
  await expect(page.getByRole('button', { name: 'Edit task title' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Edit task description' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Remove task' })).toHaveCount(0);

  if (!testInfo.project.name.includes('mobile')) {
    await page.getByRole('button', { name: 'Full screen' }).click();
    await expect(page).toHaveURL(/#\/tasks\/E2E-0003\?track=OPS$/);
  }
  await page.reload();
  await expect(page.getByText('E2E-0003', { exact: true }).first()).toBeVisible();

  await page.goto('/#/tasks/E2E-0006');
  await page.getByRole('link', { name: 'E2E-0005' }).click();
  await expect(page).toHaveURL(/#\/tasks\/E2E-0005$/);

  await page.goto('/#/wiki/guides/section-1');
  await expect(page.getByRole('heading', { name: 'section-1' })).toBeVisible();
  await page.getByRole('link', { name: /Wiki page 2/ }).click();
  await expect(page.getByRole('heading', { name: 'Wiki page 2' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Edit' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Metadata' })).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Search wiki' })).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Wiki page 2' })).toBeVisible();

  const parsed = requests.map((url) => new URL(url));
  expect(parsed.every((url) => url.hostname === '127.0.0.1' || url.hostname === 'localhost')).toBe(
    true,
  );
  expect(parsed.some((url) => url.pathname.startsWith('/api/'))).toBe(false);
  expect(parsed.filter((url) => url.pathname.endsWith('/pm-snapshot.json')).length).toBeGreaterThan(
    0,
  );
});

test('mutation-only hash routes redirect to their read views', async ({ page }) => {
  await page.goto('/#/tasks/new');
  await expect(page).toHaveURL(/#\/tasks$/);
  await page.goto('/#/tasks/settings');
  await expect(page).toHaveURL(/#\/tasks$/);
  await page.goto('/#/wiki/edit/welcome');
  await expect(page).toHaveURL(/#\/wiki\/welcome$/);
  await expect(page.getByRole('heading', { name: 'Wiki page 1' })).toBeVisible();
});

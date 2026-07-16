import { expect, test } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

import { projectRoot, resetFixture } from '../scripts/e2e-fixture.mjs';

test.beforeEach(async () => {
  await resetFixture('small');
});

test('routes, filters, deep-link fallback, and theme persistence', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/tasks$/);
  await expect(page.getByRole('heading', { name: 'Tasks' })).toBeAttached();

  await page.getByLabel('Track').selectOption('OPS');
  await expect(page).toHaveURL(/track=OPS/);
  await expect(page.locator('[pmTaskRow]')).toHaveCount(2);
  await page.getByRole('button', { name: 'Clear filters' }).click();
  await expect(page).not.toHaveURL(/track=/);

  await page.goto('/tasks/E2E-0001?state=todo');
  await expect(page).toHaveURL(/\/tasks\/E2E-0001\?state=todo$/);
  await expect(page.getByText('E2E-0001', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Close task dialog' }).click();

  const theme = page.getByRole('button', { name: /Theme:/ });
  await theme.click();
  await expect(page.locator('html')).toHaveAttribute('data-theme-preference', 'light');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme-preference', 'light');
});

test('task search composes filters, preserves board context, and handles text and empty results', async ({
  page,
}) => {
  await page.goto('/tasks?state=todo');
  const search = page.getByRole('combobox', { name: 'Search tasks' });
  const mobileSearch = page.getByRole('button', { name: 'Search tasks' });
  if (await mobileSearch.isVisible()) await mobileSearch.click();
  await search.fill('milestone:current track:E2E');
  const result = page.getByRole('option').filter({ hasText: 'E2E-0001' });
  await expect(result).toBeVisible();
  await result.click();
  await expect(page).toHaveURL(/\/tasks\/E2E-0001\?state=todo$/);
  await page.getByRole('button', { name: 'Close task dialog' }).click();

  if (await mobileSearch.isVisible()) await mobileSearch.click();
  await search.fill('Fixture task 2');
  await expect(page.getByRole('option').filter({ hasText: 'E2E-0002' })).toBeVisible();
  await search.fill('definitely-no-such-task');
  await expect(page.getByText('No matching tasks.')).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  if (!(await search.isVisible())) await mobileSearch.click();
  await expect(search).toBeVisible();
  await search.fill('id:E2E-0003');
  await expect(page.getByRole('option').filter({ hasText: 'E2E-0003' })).toBeVisible();
});

test('creates, opens, edits, moves, conflicts, and removes a task', async ({ page }) => {
  await page.goto('/tasks/new');
  await page.getByLabel('Title').fill('Created in Playwright');
  await page.getByRole('button', { name: 'Create task' }).click();
  await expect(page).toHaveURL(/\/tasks\/E2E-1000$/);
  await expect(page.getByRole('heading', { name: 'Created in Playwright' })).toBeVisible();

  await page.getByRole('button', { name: 'Edit' }).click();
  await page.locator('#edit-task-title').fill('Edited in Playwright');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: 'Edited in Playwright' })).toBeVisible();
  await page.locator('#task-state').selectOption('in-progress');
  await expect(page.locator('#task-state')).toHaveValue('in-progress');

  await page.getByRole('button', { name: 'Edit' }).click();
  await page.locator('#edit-task-title').fill('Draft title');
  const taskPath = join(projectRoot, '.pm', 'tasks', 'E2E-1000.md');
  const external = `${await readFile(taskPath, 'utf8')}\nExternal change.\n`;
  await writeFile(taskPath, external);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('This task changed elsewhere.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Review latest' }).click();
  await page.getByRole('button', { name: /Keep latest/ }).click();
  await page.getByRole('button', { name: 'Cancel' }).click();

  await page.getByRole('button', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'Remove task' }).click();
  await expect(page).toHaveURL(/\/tasks$/);
  await expect(page.getByText('Edited in Playwright')).toHaveCount(0);
});

test('protects dirty navigation and supports wiki create, edit, rename, and delete', async ({
  page,
}) => {
  await page.goto('/wiki/new');
  await page.getByLabel('Path').fill('notes/playwright');
  await page.getByLabel('Title').fill('Playwright notes');
  await page.getByRole('link', { name: 'Tasks' }).click();
  await expect(page.getByText('Discard wiki draft?')).toBeVisible();
  await page.getByRole('button', { name: 'Cancel' }).click();

  await page.getByRole('button', { name: 'Create page' }).click();
  await expect(page).toHaveURL(/\/wiki\/notes\/playwright$/);
  await page.getByRole('link', { name: 'Edit' }).click();
  await page
    .getByRole('textbox', { name: 'Wiki page Markdown body' })
    .fill('# Updated\n\nEdited locally.');
  await page.getByRole('button', { name: 'Save body' }).click();
  await expect(page.getByRole('heading', { name: 'Updated' })).toBeVisible();

  await page.getByRole('link', { name: 'Metadata' }).click();
  await page.getByLabel('Path').fill('notes/renamed');
  await page.getByLabel('Title').fill('Renamed notes');
  await page.getByRole('button', { name: 'Save metadata' }).click();
  await expect(page).toHaveURL(/\/wiki\/notes\/renamed$/);
  await page.getByRole('link', { name: 'Metadata' }).click();
  await page.getByRole('button', { name: 'Delete page' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete page' }).click();
  await expect(page).toHaveURL(/\/wiki$/);
});

test('shows settings validation and protects required configuration', async ({ page }) => {
  await page.goto('/tasks/settings');
  await expect(page.getByRole('heading', { name: 'Project settings' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Statuses' })).toBeVisible();

  const expectInUseRemovalRejected = async (
    heading: string,
    key: string,
    label: string,
    kind: 'status' | 'track' | 'milestone',
  ) => {
    const section = page.locator('section').filter({
      has: page.getByRole('heading', { name: heading }),
    });
    const row = section.getByRole('listitem').filter({
      has: page.getByText(key, { exact: true }),
    });
    await row.getByRole('button', { name: `Remove ${kind}` }).click();
    const dialog = page.getByRole('dialog', { name: 'Remove project setting' });
    await dialog.getByRole('button', { name: 'Remove', exact: true }).click();
    await expect(row.getByRole('alert')).toContainText(
      `${kind[0]!.toUpperCase()}${kind.slice(1)} ${key} is referenced by one or more tasks.`,
    );
    await expect(row.getByText(key, { exact: true })).toBeVisible();
    await expect(row.getByText(label, { exact: true })).toBeVisible();
  };

  await expectInUseRemovalRejected('Statuses', 'todo', 'To Do', 'status');
  await expectInUseRemovalRejected('Tracks', 'E2E', 'Product', 'track');
  await expectInUseRemovalRejected('Milestones', 'current', 'Current Release', 'milestone');

  await page.getByRole('button', { name: 'Add status' }).click();
  await expect(page.getByRole('button', { name: 'Add status', exact: true }).last()).toBeDisabled();
  await page.getByLabel('Key').fill('review');
  await page.locator('#status-name').fill('Review');
  await page.getByRole('button', { name: 'Add status', exact: true }).last().click();
  await expect(page.getByText('Review', { exact: true })).toBeVisible();
});

test('large project remains dense, navigable, and free of horizontal page overflow', async ({
  page,
}) => {
  await resetFixture('large');
  const started = Date.now();
  await page.goto('/tasks?state=done');
  await expect(page.locator('[pmTaskRow]')).toHaveCount(120, { timeout: 10_000 });
  expect(Date.now() - started).toBeLessThan(10_000);

  await page.goto('/wiki');
  await expect(page.locator('.wiki-list-row')).toHaveCount(48);
  await page.getByRole('link', { name: 'Wiki page 48' }).first().click();
  await expect(page.getByRole('heading', { name: 'Wiki page 48' })).toBeVisible();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(1);
});

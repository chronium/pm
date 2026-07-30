import { expect, test } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

import { projectRoot, resetFixture } from '../scripts/e2e-fixture.mjs';
import {
  acceptedRun,
  readyPreflight,
  runArtifacts,
  runEvents,
  runInspection,
  runnerRegistration,
  runnerStatus,
} from '../src/app/agent-runs/agent-runs.fixtures';

test.beforeEach(async () => {
  await resetFixture('small');
});

test('routes, filters, deep-link fallback, and theme persistence', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/tasks$/);
  await expect(page.getByRole('heading', { name: 'Tasks' })).toBeAttached();

  const menu = page.getByRole('button', { name: 'Toggle navigation' });
  if (await menu.isVisible()) await menu.click();
  await page.getByRole('link', { name: /Operations/ }).click();
  await expect(page).toHaveURL(/track=OPS/);
  await expect(page.locator('[pmTaskRow]')).toHaveCount(2);
  if (await menu.isVisible()) await menu.click();
  await page.getByRole('link', { name: /All tasks/ }).click();
  await expect(page).not.toHaveURL(/track=/);

  await page.goto('/tasks/E2E-0001?state=todo');
  await expect(page).toHaveURL(/\/tasks\/E2E-0001\?state=todo$/);
  await expect(page.getByText('E2E-0001', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Back' }).click();

  const theme = page.getByRole('button', { name: /Theme:/ });
  await theme.click();
  await expect(page.locator('html')).toHaveAttribute('data-theme-preference', 'light');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme-preference', 'light');
  await page.getByRole('button', { name: /Theme:/ }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme-preference', 'dark');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme-preference', 'dark');
});

test('task search follows sidebar scope, supports in:all, and preserves board context', async ({
  page,
}, testInfo) => {
  await page.goto('/tasks?track=OPS&state=todo');
  await expect(page.locator('form[aria-label="Board filters"]')).toHaveCount(0);
  const search = page.getByRole('combobox', { name: 'Search tasks' });
  const mobileSearch = page.getByRole('button', { name: 'Search tasks' });
  if (await mobileSearch.isVisible()) await mobileSearch.click();
  await search.fill('Fixture task');
  await expect(page.getByRole('option').filter({ hasText: 'E2E-0001' })).toHaveCount(0);
  const result = page.getByRole('option').filter({ hasText: 'E2E-0003' });
  await expect(result).toBeVisible();
  await result.click();
  const mobileProject = testInfo.project.name.includes('mobile');
  await expect(page).toHaveURL(
    mobileProject
      ? /\/tasks\/E2E-0003\?track=OPS&state=todo$/
      : /\/tasks\/dialog\/E2E-0003\?track=OPS&state=todo$/,
  );
  await page
    .getByRole('button', { name: mobileProject ? 'Back' : 'Close task dialog', exact: true })
    .click();
  await expect(page).toHaveURL(/\/tasks\?track=OPS&state=todo$/);

  if (await mobileSearch.isVisible()) await mobileSearch.click();
  await search.fill('Fixture task 2 in:all state:todo');
  await expect(page.getByRole('option').filter({ hasText: 'E2E-0002' })).toBeVisible();
  await search.fill('definitely-no-such-task');
  await expect(page.getByText('No matching tasks.')).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('form[aria-label="Board filters"]')).toHaveCount(0);
  if (!(await search.isVisible())) await mobileSearch.click();
  await search.fill('id:E2E-0003 in:selection');
  await expect(page.getByRole('option').filter({ hasText: 'E2E-0003' })).toBeVisible();
});

test('opens the next dependency-ready task within the active sidebar scope', async ({
  page,
}, testInfo) => {
  await page.goto('/tasks?track=E2E&milestone=current');
  const menu = page.getByRole('button', { name: 'Toggle navigation' });
  if (await menu.isVisible()) await menu.click();

  await page.getByRole('button', { name: 'Next task' }).click();

  await expect(page).toHaveURL(
    testInfo.project.name.includes('mobile')
      ? /\/tasks\/E2E-0001\?track=E2E&milestone=current$/
      : /\/tasks\/dialog\/E2E-0001\?track=E2E&milestone=current$/,
  );
  await expect(page.getByRole('heading', { name: 'Recommendation' })).toBeVisible();
  await expect(page.locator('.recommendation-context')).toContainText(
    'Selected high priority task',
  );
});

test('uses desktop overlays, fullscreen replacement, and mobile canonical pages', async ({
  page,
}, testInfo) => {
  test.skip(testInfo.project.name.includes('mobile'), 'This test switches from desktop to mobile.');
  await page.goto('/tasks?track=E2E');
  await page.getByRole('link', { name: /E2E-0001/ }).click();
  await expect(page).toHaveURL(/\/tasks\/dialog\/E2E-0001\?track=E2E$/);
  await expect(page.getByRole('dialog')).toBeVisible();
  const scrollContainment = await page.evaluate(() => {
    const outer = document.querySelector<HTMLElement>('.task-dialog-scroll')!;
    const description = document.querySelector<HTMLElement>('.description-section')!;
    const columns = document.querySelector<HTMLElement>('.workspace-columns')!;
    const metadata = document.querySelector<HTMLElement>('.metadata-column')!;
    const actions = document.querySelector<HTMLElement>('.host-actions')!;
    const filler = document.createElement('div');
    filler.style.height = '2000px';
    description.append(filler);
    const actionTop = actions.getBoundingClientRect().top;
    description.scrollTop = description.scrollHeight;
    const result = {
      outerClientHeight: outer.clientHeight,
      outerScrollHeight: outer.scrollHeight,
      outerScrollTop: outer.scrollTop,
      descriptionClientHeight: description.clientHeight,
      descriptionScrollHeight: description.scrollHeight,
      descriptionScrollTop: description.scrollTop,
      columnGap: getComputedStyle(columns).columnGap,
      descriptionRight: description.getBoundingClientRect().right,
      metadataLeft: metadata.getBoundingClientRect().left,
      actionTop,
      actionTopAfterScroll: actions.getBoundingClientRect().top,
    };
    filler.remove();
    return result;
  });
  expect(scrollContainment.outerScrollHeight).toBe(scrollContainment.outerClientHeight);
  expect(scrollContainment.outerScrollTop).toBe(0);
  expect(scrollContainment.descriptionScrollHeight).toBeGreaterThan(
    scrollContainment.descriptionClientHeight,
  );
  expect(scrollContainment.descriptionScrollTop).toBeGreaterThan(0);
  expect(scrollContainment.columnGap).toBe('0px');
  expect(scrollContainment.metadataLeft).toBeCloseTo(scrollContainment.descriptionRight, 3);
  expect(scrollContainment.actionTopAfterScroll).toBe(scrollContainment.actionTop);
  await page.getByRole('button', { name: 'Full screen' }).click();
  await expect(page).toHaveURL(/\/tasks\/E2E-0001\?track=E2E$/);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('button', { name: 'Back' }).click();
  await expect(page).toHaveURL(/\/tasks\?track=E2E$/);

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('link', { name: /E2E-0001/ }).click();
  await expect(page).toHaveURL(/\/tasks\/E2E-0001\?track=E2E$/);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  const contextLine = await page.locator('.task-context-line').boundingBox();
  const mobileActions = await page.locator('.host-actions').boundingBox();
  expect(mobileActions!.x + mobileActions!.width).toBeCloseTo(
    contextLine!.x + contextLine!.width,
    3,
  );
  const description = await page.locator('.description-section').boundingBox();
  const metadata = await page.locator('.metadata-column').boundingBox();
  expect(metadata!.y).toBeGreaterThanOrEqual(description!.y + description!.height);
  const mobileFlow = await page.evaluate(() => {
    const content = document.querySelector<HTMLElement>('.content')!;
    const descriptionSection = document.querySelector<HTMLElement>('.description-section')!;
    return {
      contentClientHeight: content.clientHeight,
      contentScrollHeight: content.scrollHeight,
      descriptionClientHeight: descriptionSection.clientHeight,
      descriptionScrollHeight: descriptionSection.scrollHeight,
    };
  });
  expect(mobileFlow.contentScrollHeight).toBeGreaterThan(mobileFlow.contentClientHeight);
  expect(mobileFlow.descriptionScrollHeight).toBe(mobileFlow.descriptionClientHeight);
});

test('wiki search finds body content and opens a nested page', async ({ page }) => {
  await page.goto('/wiki');
  const search = page.getByRole('combobox', { name: 'Search wiki' });
  const mobileSearch = page.getByRole('button', { name: 'Search wiki' });
  if (await mobileSearch.isVisible()) await mobileSearch.click();
  await search.fill('Local fixture content');
  const result = page.getByRole('option').filter({ hasText: 'Wiki page 2' });
  await expect(result).toBeVisible();
  await result.click();
  await expect(page).toHaveURL(/\/wiki\/guides\/section-1\/page-2$/);
  await expect(page.getByRole('heading', { name: 'Wiki page 2' })).toBeVisible();
  await expect(page.locator('pm-wiki-search input')).toHaveValue('');
});

test('creates, opens, edits, moves, conflicts, and removes a task', async ({ page }) => {
  await page.goto('/tasks/new');
  await page.getByLabel('Title').fill('Created in Playwright');
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page).toHaveURL(/\/tasks\/E2E-1000$/);
  await expect(page.getByRole('button', { name: 'Edit task title' })).toHaveText(
    'Created in Playwright',
  );

  await page.getByRole('button', { name: 'Edit task title' }).click();
  await page.locator('#workspace-title').fill('Edited in Playwright');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Edit task title' })).toHaveText(
    'Edited in Playwright',
  );
  await page.getByRole('button', { name: 'Edit task status' }).click();
  await page.locator('#workspace-status').selectOption('in-progress');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Edit task status' })).toContainText('In Progress');

  await page.getByRole('button', { name: 'Add task note' }).click();
  await page.locator('#task-note').fill('Added from the task workspace.');
  await page.getByRole('button', { name: 'Add note', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Notes' })).toBeVisible();
  await expect(page.getByText('Added from the task workspace.')).toBeVisible();

  await page.getByRole('button', { name: 'Edit task title' }).click();
  await page.locator('#workspace-title').fill('Draft title');
  await page.locator('.property-field').filter({ hasText: 'Track' }).getByRole('button').click();
  await page.locator('#workspace-track').selectOption('OPS');
  const taskPath = join(projectRoot, '.pm', 'tasks', 'E2E-1000.md');
  const external = `${await readFile(taskPath, 'utf8')}\nExternal change.\n`;
  await writeFile(taskPath, external);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('This task changed elsewhere.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Review latest' }).click();
  await page.getByRole('button', { name: 'Restore draft' }).click();
  await expect(page.locator('#workspace-track')).toHaveValue('OPS');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Edit task title' })).toHaveText('Draft title');
  await expect(page.getByRole('button', { name: 'Edit task track' })).toContainText('Operations');

  await page.locator('.remove-action').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Remove task' }).click();
  await expect(page).toHaveURL(/\/tasks$/);
  await expect(page.getByText('Edited in Playwright')).toHaveCount(0);
});

test('editing placement refreshes a scoped board while keeping its dialog and scope', async ({
  page,
}, testInfo) => {
  test.skip(
    testInfo.project.name.includes('mobile'),
    'Scoped board refresh is a desktop overlay flow.',
  );
  await page.goto('/tasks?track=E2E&milestone=current');
  await page.getByRole('link', { name: /E2E-0001/ }).click();
  await expect(page).toHaveURL(/\/tasks\/dialog\/E2E-0001\?track=E2E&milestone=current$/);
  await expect(page.getByRole('button', { name: 'Edit task title' })).toHaveText('Fixture task 1');

  await page.getByRole('button', { name: 'Edit task track' }).click();
  await page.locator('#workspace-track').selectOption('OPS');
  await page.getByRole('button', { name: 'Edit task milestone' }).click();
  await page.locator('#workspace-milestone').selectOption('later');
  await page.getByRole('button', { name: 'Save', exact: true }).click();

  await expect(page).toHaveURL(/\/tasks\/dialog\/E2E-0001\?track=E2E&milestone=current$/);
  await expect(page.getByRole('button', { name: 'Edit task title' })).toHaveText('Fixture task 1');
  await expect(page.getByRole('button', { name: 'Edit task track' })).toContainText('Operations');
  await page.getByRole('button', { name: 'Close task dialog', exact: true }).click();
  await expect(page).toHaveURL(/\/tasks\?track=E2E&milestone=current$/);
  await expect(page.getByText('Fixture task 1', { exact: true })).toHaveCount(0);
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

test('wiki Markdown workspace splits on desktop and preserves mobile pane state', async ({
  page,
}, testInfo) => {
  await page.goto('/wiki/edit/welcome');
  const editor = page.getByRole('textbox', { name: 'Wiki page Markdown body' });
  const markdown = Array.from(
    { length: 36 },
    (_, index) => `## Workspace section ${index + 1}\n\nScrollable preview content ${index + 1}.`,
  ).join('\n\n');
  await editor.fill(markdown);

  const editorPane = page.locator('.wiki-workspace-editor');
  const previewPane = page.locator('.wiki-workspace-preview');
  const editorScroll = page.locator('.CodeMirror-scroll');
  if (!testInfo.project.name.includes('mobile')) {
    await expect(page.getByRole('heading', { name: 'Workspace section 36' })).toBeVisible();
    const editorBox = await editorPane.boundingBox();
    const previewBox = await previewPane.boundingBox();
    expect(editorBox).not.toBeNull();
    expect(previewBox).not.toBeNull();
    expect(Math.abs(editorBox!.width - previewBox!.width)).toBeLessThanOrEqual(1);
    expect(Math.abs(editorBox!.height - previewBox!.height)).toBeLessThanOrEqual(1);
    await editorScroll.evaluate((element) => (element.scrollTop = element.scrollHeight));
    expect(await editorScroll.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
    expect(await previewPane.evaluate((element) => element.scrollTop)).toBe(0);
    await previewPane.evaluate((element) => (element.scrollTop = element.scrollHeight));
    expect(await previewPane.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
    expect(await editorScroll.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
    await expect(page.getByRole('button', { name: 'Preview' })).toHaveCount(0);
    return;
  }

  const editorTab = page.getByRole('tab', { name: 'Editor' });
  const previewTab = page.getByRole('tab', { name: 'Preview' });
  await expect(editorTab).toHaveAttribute('aria-selected', 'true');
  await editorScroll.evaluate((element) => (element.scrollTop = element.scrollHeight));
  const editorPosition = await editorScroll.evaluate((element) => element.scrollTop);
  expect(editorPosition).toBeGreaterThan(0);
  await editorTab.focus();
  await editorTab.press('ArrowRight');
  await expect(previewTab).toBeFocused();
  await expect(previewTab).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('heading', { name: 'Workspace section 36' })).toBeVisible();
  await previewPane.evaluate((element) => (element.scrollTop = element.scrollHeight));
  const previewPosition = await previewPane.evaluate((element) => element.scrollTop);
  expect(previewPosition).toBeGreaterThan(0);
  await editorTab.click();
  await expect(page.locator('.CodeMirror-code')).toContainText('Workspace section 36');
  await expect
    .poll(() => editorScroll.evaluate((element) => element.scrollTop))
    .toBe(editorPosition);
  await previewTab.click();
  await expect
    .poll(() => previewPane.evaluate((element) => element.scrollTop))
    .toBe(previewPosition);
});

test('shows settings validation and protects required configuration', async ({ page }) => {
  await page.goto('/tasks/settings');
  await expect(page.getByRole('heading', { name: 'Project settings' })).toBeVisible();

  await page.getByRole('button', { name: 'Members' }).click();
  await expect(page.getByRole('heading', { name: 'Project members' })).toBeVisible();
  await expect(page.getByText('Authenticated with the project service')).toBeVisible();

  await page.getByRole('button', { name: 'Statuses' }).click();
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

  await page.getByRole('button', { name: 'Tracks' }).click();
  await expectInUseRemovalRejected('Tracks', 'E2E', 'Product', 'track');

  await page.getByRole('button', { name: 'Milestones' }).click();
  await expectInUseRemovalRejected('Milestones', 'current', 'Current Release', 'milestone');

  await page.getByRole('button', { name: 'Statuses' }).click();
  await page.getByRole('button', { name: 'Add status' }).click();
  await expect(page.getByRole('button', { name: 'Add status', exact: true }).last()).toBeDisabled();
  await page.getByLabel('Key').fill('review');
  await page.locator('#status-name').fill('Review');
  await page.getByRole('button', { name: 'Add status', exact: true }).last().click();
  await expect(page.getByText('Review', { exact: true })).toBeVisible();
});

test('pairs a runner, starts one immutable task run, and supervises its durable output', async ({
  page,
}, testInfo) => {
  let paired = false;
  let starts = 0;
  let collections = 0;
  const artifactContent = 'hello';
  const downloadableArtifact = {
    ...runArtifacts[0]!,
    byteLength: artifactContent.length,
    sha256: '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824',
  };
  const journal = [
    ...runEvents,
    ...Array.from({ length: 1989 }, (_, index) => ({
      ...runEvents[0]!,
      sequence: index + 9,
      timestamp: '2026-07-29T08:04:00.000Z',
      type: 'command.output',
      state: 'running' as const,
      summary: 'Command output',
      data: { output: `Structured output ${index + 9}` },
    })),
    {
      ...runEvents[0]!,
      sequence: 1998,
      timestamp: '2026-07-29T08:03:09.000Z',
      type: 'run.state_changed',
      state: 'validating' as const,
      summary: 'Validating changes',
    },
    {
      ...runEvents[0]!,
      sequence: 1999,
      timestamp: '2026-07-29T08:03:10.000Z',
      type: 'run.state_changed',
      state: 'collecting_artifacts' as const,
      summary: 'Collecting artifacts',
    },
    {
      ...runEvents[0]!,
      sequence: 2000,
      timestamp: '2026-07-29T08:03:11.000Z',
      type: 'run.state_changed',
      state: 'completed' as const,
      summary: 'Run completed',
    },
  ];
  const completedInspection = {
    ...runInspection,
    run: {
      ...runInspection.run,
      state: 'completed' as const,
      lastEventSequence: journal.length,
      updatedAt: '2026-07-29T08:10:00.000Z',
      terminalAt: '2026-07-29T08:10:00.000Z',
    },
  };
  await page.route('**/api/v1/runners**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/v1/runners/pair' && request.method() === 'POST') {
      expect(request.postDataJSON()).toMatchObject({
        runnerId: runnerRegistration.runnerId,
        pairingCode: 'one-use-code',
      });
      paired = true;
      await route.fulfill({ status: 201, json: runnerRegistration });
      return;
    }
    if (path.endsWith('/status')) {
      await route.fulfill({ json: runnerStatus });
      return;
    }
    if (path === '/api/v1/runners') {
      await route.fulfill({ json: paired ? [runnerRegistration] : [] });
      return;
    }
    await route.fulfill({ status: 204 });
  });
  await page.route('**/api/v1/runs/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/v1/runs/preflight') {
      expect(request.postDataJSON()).toMatchObject({
        taskId: 'E2E-0001',
        runnerId: runnerRegistration.runnerId,
        profileId: 'pm-development',
        providerId: 'codex',
        modelId: 'gpt-5.4',
        effortId: 'medium',
      });
      await route.fulfill({ json: readyPreflight, headers: { ETag: '"draft-r1"' } });
      return;
    }
    if (path === '/api/v1/runs/run-01K123/start') {
      starts += 1;
      expect(request.headers()['if-match']).toBe('"draft-r1"');
      await route.fulfill({ status: 202, json: acceptedRun });
      return;
    }
    if (path === '/api/v1/runs/run-01K123') {
      await route.fulfill({ json: completedInspection });
      return;
    }
    if (path === '/api/v1/runs/run-01K123/events') {
      await route.fulfill({
        json: {
          events: journal,
          nextAfterSequence: journal.length,
          hasMore: false,
          terminal: false,
        },
      });
      return;
    }
    if (path === '/api/v1/runs/run-01K123/artifacts') {
      await route.fulfill({ json: [downloadableArtifact] });
      return;
    }
    if (path === '/api/v1/runs/run-01K123/artifacts/changes-patch/content') {
      await route.fulfill({
        body: artifactContent,
        contentType: downloadableArtifact.mediaType,
        headers: {
          'Content-Length': String(downloadableArtifact.byteLength),
          'PM-Artifact-Id': downloadableArtifact.artifactId,
          'PM-Artifact-SHA256': downloadableArtifact.sha256,
          ETag: `"sha256:${downloadableArtifact.sha256}"`,
        },
      });
      return;
    }
    if (path === '/api/v1/runs/run-01K123/patch-collection/preflight') {
      await route.fulfill({
        headers: { ETag: '"patch-r1"' },
        json: {
          ready: true,
          revision: 'patch-r1',
          artifactId: downloadableArtifact.artifactId,
          artifactSha256: downloadableArtifact.sha256,
          baseCommit: completedInspection.run.specification.repository.baseCommit,
          currentHead: completedInspection.run.specification.repository.baseCommit,
          taskRevision: completedInspection.run.specification.task.revision,
          currentTaskRevision: completedInspection.run.specification.task.revision,
          checks: [
            {
              id: 'base',
              label: 'Exact base commit',
              status: 'passed',
              summary: 'Base matches.',
            },
          ],
          warnings: [],
          paths: [
            {
              path: 'PM/TaskService.cs',
              status: 'modified',
              insertions: 3,
              deletions: 1,
              binary: false,
            },
          ],
          statistics: { filesChanged: 1, insertions: 3, deletions: 1, binaryFiles: 0 },
        },
      });
      return;
    }
    if (path === '/api/v1/runs/run-01K123/patch-collection/apply') {
      expect(request.headers()['if-match']).toBe('"patch-r1"');
      expect(request.postDataJSON()).toEqual({ artifactSha256: downloadableArtifact.sha256 });
      collections += 1;
      await route.fulfill({
        json: {
          runId: 'run-01K123',
          artifactId: downloadableArtifact.artifactId,
          artifactSha256: downloadableArtifact.sha256,
          baseCommit: completedInspection.run.specification.repository.baseCommit,
          headCommit: completedInspection.run.specification.repository.baseCommit,
          paths: ['PM/TaskService.cs'],
          appliedAt: '2026-07-29T08:11:00.000Z',
        },
      });
      return;
    }
    await route.fulfill({ status: 404 });
  });

  await page.goto('/tasks/settings');
  await page.getByRole('button', { name: 'Agent runners' }).click();
  await page.getByRole('button', { name: 'Pair runner' }).first().click();
  const pairing = page.getByRole('dialog', { name: 'Pair agent runner' });
  await pairing.getByLabel('HTTPS endpoint').fill(runnerRegistration.endpoint);
  await pairing.getByLabel('Runner ID', { exact: true }).fill(runnerRegistration.runnerId);
  await pairing.getByLabel('TLS SHA-256 fingerprint').fill(runnerRegistration.tlsFingerprint);
  await pairing.getByLabel('One-time pairing code').fill('one-use-code');
  await pairing.getByRole('button', { name: 'Pair runner' }).click();
  await expect(page.getByText('Linux workstation')).toBeVisible();
  await expect(page.getByText('1/3 active')).toBeVisible();

  await page.goto('/tasks/E2E-0001');
  await page.getByRole('button', { name: 'Run with Codex' }).click();
  const launch = page.getByRole('dialog', { name: 'Run with Codex' });
  await expect(launch.getByText('Open network profile')).toBeVisible();
  await launch.getByRole('button', { name: 'Check readiness' }).click();
  await expect(launch.getByText('Ready to start.')).toBeVisible();
  await expect(launch.getByText('1234567890abcdef1234567890abcdef12345678')).toBeVisible();
  await launch.getByRole('button', { name: 'Start run' }).click();
  await expect(page).toHaveURL(/\/tasks\/runs\/run-01K123$/);
  await expect(page.getByText('Completed', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('changes.patch')).toBeVisible();
  const artifactDownload = page.waitForEvent('download');
  await page
    .getByRole('region', { name: 'Artifacts' })
    .getByRole('button', { name: 'Download', exact: true })
    .click();
  expect((await artifactDownload).suggestedFilename()).toBe('changes.patch');
  await expect(page.getByText('Download verified.')).toBeVisible();
  await page.getByRole('button', { name: 'Review & collect' }).click();
  const collection = page.getByRole('dialog', { name: 'Review patch collection' });
  await expect(collection.getByText('PM/TaskService.cs')).toBeVisible();
  await collection.getByRole('button', { name: 'Collect patch' }).click();
  await expect(page.getByText('Collected 1 changed path into the local worktree.')).toBeVisible();
  if (testInfo.project.name.includes('mobile')) {
    await page.getByRole('tab', { name: 'Output' }).click();
  }
  await expect(page.getByText('Structured output 1997')).toBeVisible();
  const virtualRows = page.locator('.log-row');
  await expect(virtualRows).not.toHaveCount(0);
  expect(await virtualRows.count()).toBeLessThan(100);
  const scroll = await page.locator('.log-viewport').evaluate((viewport) => ({
    clientHeight: viewport.clientHeight,
    scrollHeight: viewport.scrollHeight,
  }));
  expect(scroll.scrollHeight).toBeGreaterThan(scroll.clientHeight);
  expect(starts).toBe(1);
  expect(collections).toBe(1);
});

test('shows actionable diagnostics for a failed remote run', async ({ page }, testInfo) => {
  const failure = {
    code: 'repository_fetch_failed',
    stage: 'workspace',
    summary: 'The runner could not fetch the repository.',
    recommendedAction:
      'Check runner network access and repository credentials, then launch a new run.',
    retryable: true,
  };
  const failedEvent = {
    ...runEvents[0]!,
    sequence: 4,
    state: 'failed' as const,
    summary: failure.summary,
    data: { failure },
  };
  await page.route('**/api/v1/runs/run-failed**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/events')) {
      await route.fulfill({
        json: {
          events: [...runEvents.slice(0, 3), failedEvent],
          nextAfterSequence: 4,
          hasMore: false,
          terminal: true,
        },
      });
      return;
    }
    if (path.endsWith('/artifacts')) {
      await route.fulfill({ json: [] });
      return;
    }
    await route.fulfill({
      json: {
        ...runInspection,
        run: {
          ...runInspection.run,
          runId: 'run-failed',
          state: 'failed',
          lastEventSequence: 4,
          terminalAt: '2026-07-29T08:10:00.000Z',
        },
      },
    });
  });

  await page.goto('/tasks/runs/run-failed');
  await expect(page.getByText('repository_fetch_failed', { exact: true })).toBeVisible();
  await expect(page.getByText(failure.recommendedAction, { exact: true })).toBeVisible();
  if (testInfo.project.name.includes('mobile')) {
    await page.getByRole('tab', { name: 'Output' }).click();
  }
  await expect(page.getByText(/Recommended action: Check runner network access/)).toBeVisible();
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

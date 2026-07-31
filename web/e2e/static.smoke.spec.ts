import { expect, test } from '@playwright/test';

test('static snapshot supports filters, task views, dependencies, wiki folders, and hash reloads', async ({
  page,
}, testInfo) => {
  const requests: string[] = [];
  page.on('request', (request) => requests.push(request.url()));

  await page.goto('/#/tasks?track=OPS');
  await expect(page.locator('.snapshot-context')).toBeVisible();
  await expect(page.locator('.snapshot-context')).toContainText('Read-only');
  const modeCount = page.locator('.mode-count');
  await expect(modeCount).toBeVisible();
  await expect(modeCount).toHaveCSS('white-space', 'nowrap');
  if (testInfo.project.name.includes('mobile')) {
    await expect(modeCount.locator('.mode-count-suffix')).toBeHidden();
    const headerLayout = await page.locator('.topbar').evaluate((topbar) => {
      const count = topbar.querySelector<HTMLElement>('.mode-count')!;
      const countStyle = getComputedStyle(count);
      return {
        countHeight: count.getBoundingClientRect().height,
        countLineHeight: Number.parseFloat(countStyle.lineHeight),
        topbarClientWidth: topbar.clientWidth,
        topbarScrollWidth: topbar.scrollWidth,
      };
    });
    expect(headerLayout.countHeight).toBeLessThanOrEqual(headerLayout.countLineHeight + 1);
    expect(headerLayout.topbarScrollWidth).toBeLessThanOrEqual(headerLayout.topbarClientWidth);
  } else {
    await expect(modeCount.locator('.mode-count-suffix')).toBeVisible();
    const tasksLabel = await page
      .getByRole('link', { name: /Tasks \d+ tasks left/ })
      .getByText('Tasks', { exact: true })
      .boundingBox();
    const boardSurface = await page.locator('.pm-board-surface').boundingBox();
    const workspaceInset = tasksLabel!.x - boardSurface!.x;
    expect(workspaceInset).toBeGreaterThanOrEqual(8);
    expect(workspaceInset).toBeLessThanOrEqual(16);
  }
  await page.getByRole('button', { name: /switch project/i }).click();
  const projectMenu = page.locator('.project-switcher-menu');
  await expect(projectMenu).toBeVisible();
  const menuReceivesPointer = await projectMenu.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    const target = document.elementFromPoint(bounds.left + 8, bounds.top + 8);
    return target !== null && element.contains(target);
  });
  expect(menuReceivesPointer).toBe(true);
  const publishedProject = page.getByRole('link', { name: /published child/i });
  await expect(publishedProject).toBeVisible();
  await expect(publishedProject).toHaveAttribute('href', /\/published\/\?source=fixture#old$/);
  await expect(page.locator('.project-switcher-unavailable')).toHaveAttribute(
    'title',
    /does not publish/,
  );

  const taskSearch = page.getByRole('combobox', { name: 'Search tasks' });
  const mobileTaskSearch = page.getByRole('button', { name: 'Search tasks' });
  if (await mobileTaskSearch.isVisible()) {
    await expect(mobileTaskSearch).toBeVisible();
    await mobileTaskSearch.click();
  } else {
    await expect(taskSearch).toBeVisible();
  }
  await expect(page.getByRole('link', { name: 'New task' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Settings' })).toHaveCount(0);
  await expect(page.locator('[pmTaskRow]')).toHaveCount(2);

  await taskSearch.fill('Fixture description for E2E-0003');
  await expect(page.getByRole('option', { name: /E2E-0003/ })).toBeVisible();
  await taskSearch.press('Enter');
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
  if (testInfo.project.name.includes('mobile')) {
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
  }

  await page.goto('/#/tasks/E2E-0006');
  await page.getByRole('link', { name: 'pm://project/playwright-project/task/E2E-0005' }).click();
  await expect(page).toHaveURL(/#\/tasks\/E2E-0005$/);

  await page.goto('/#/wiki/guides/section-1');
  await expect(page.getByRole('heading', { name: 'section-1' })).toBeVisible();
  const wikiSearch = page.getByRole('combobox', { name: 'Search wiki' });
  const mobileWikiSearch = page.getByRole('button', { name: 'Search wiki' });
  if (await mobileWikiSearch.isVisible()) {
    await expect(mobileWikiSearch).toBeVisible();
    await mobileWikiSearch.click();
  } else {
    await expect(wikiSearch).toBeVisible();
  }
  await wikiSearch.fill('Local fixture content');
  await page.getByRole('option', { name: /Wiki page 2/ }).click();
  await expect(page.getByRole('heading', { name: 'Wiki page 2' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Edit' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Metadata' })).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Wiki page 2' })).toBeVisible();

  await page.goto('/#/wiki/welcome');
  await expect(page.getByRole('link', { name: 'Current task' })).toHaveAttribute(
    'href',
    '#/tasks/E2E-0001',
  );
  await expect(page.getByRole('link', { name: 'Published wiki' })).toHaveAttribute(
    'href',
    /\/published\/\?source=fixture#\/wiki\/guide\/hello%20world$/,
  );
  await expect(page.getByText('Unavailable wiki')).toHaveAttribute('title', /does not publish/);

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

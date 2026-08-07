import { expect, test } from '@playwright/test';

test('embedded host serves deep links, API, hashed assets, and loopback-only requests', async ({
  page,
  request,
}) => {
  const nonLoopback = new Set<string>();
  page.on('request', (browserRequest) => {
    const url = new URL(browserRequest.url());
    if (url.hostname !== '127.0.0.1' && url.hostname !== 'localhost') nonLoopback.add(url.href);
  });

  const response = await page.goto('/wiki/welcome');
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'Wiki page 1' })).toBeVisible();

  const api = await request.get('/api/v1/project');
  expect(api.ok()).toBeTruthy();
  const taskDeepLink = await request.get('/tasks/E2E-0001');
  expect(taskDeepLink.ok()).toBeTruthy();

  await page.goto('/tasks/settings');
  await page.getByRole('button', { name: 'Activation', exact: true }).click();
  const manual = page.locator('details').filter({ hasText: 'Manual entry' });
  await manual.locator('summary').click();
  await manual.getByRole('button', { name: 'Activate', exact: true }).click();
  const activation = page.getByRole('dialog', { name: 'Activate Manual entry?' });
  await activation.getByRole('button', { name: 'Activate trigger' }).click();
  await expect(manual.getByText('Active manually')).toBeVisible();
  await page.reload();
  await page.getByRole('button', { name: 'Activation', exact: true }).click();
  await expect(page.locator('details').filter({ hasText: 'Manual entry' })).toContainText(
    'Active manually',
  );
  const activationApi = await request.get('/api/v1/activation');
  expect(activationApi.ok()).toBeTruthy();
  expect(await activationApi.text()).toContain('"mode":"manual"');

  const html = await (await request.get('/')).text();
  const assets = [...html.matchAll(/(?:src|href)="([^"]+\.(?:js|css))"/g)].map((match) => match[1]);
  expect(assets.length).toBeGreaterThan(0);
  expect(assets.every((asset) => /[.-][A-Za-z0-9_-]{8,}\.(?:js|css)$/.test(asset))).toBeTruthy();
  for (const asset of assets) {
    const assetResponse = await request.get(asset);
    expect(assetResponse.ok()).toBeTruthy();
    expect(assetResponse.headers()['cache-control']).toContain('immutable');
  }
  expect([...nonLoopback]).toEqual([]);
});

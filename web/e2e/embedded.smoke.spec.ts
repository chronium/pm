import { expect, test } from '@playwright/test';

test('embedded host serves deep links, API, hashed assets, redirects, and loopback-only requests', async ({
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
  const redirect = await request.get('/task/E2E-0001/edit', { maxRedirects: 0 });
  expect(redirect.status()).toBe(302);
  expect(redirect.headers()['location']).toBe('/tasks/E2E-0001');

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

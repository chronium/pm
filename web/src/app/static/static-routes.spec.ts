import { staticRoutes } from '../app.routes';

describe('static routes', () => {
  it('keeps activation settings readable while redirecting mutation-only task routes', () => {
    const tasks = staticRoutes.find((route) => route.path === 'tasks')!;
    const children = tasks.children!;
    const board = children.find((route) => route.path === '')!;

    expect(children.find((route) => route.path === 'settings')?.loadComponent).toBeDefined();
    expect(children.find((route) => route.path === 'new')?.redirectTo).toBe('');
    expect(children.find((route) => route.path === 'runs/:runId')?.redirectTo).toBe('');
    expect(board.children?.find((route) => route.path === 'dialog/new')?.redirectTo).toBe('');
    expect(children.find((route) => route.path === ':taskId')?.data?.['mode']).toBe('detail');
  });

  it('keeps only read routes in the static wiki shell', () => {
    const wiki = staticRoutes.find((route) => route.path === 'wiki')!;
    const children = wiki.children!;

    expect(children.find((route) => route.path === 'new')?.redirectTo).toBe('');
    expect(children.filter((route) => route.matcher)).toHaveLength(3);
    expect(children.filter((route) => route.loadComponent)).toHaveLength(1);
  });
});

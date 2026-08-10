import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { ProjectContextService } from './project-context.service';
import { StaticModeService } from '../static/static-mode.service';

@Component({ template: '' })
class RouteTarget {}

describe('ProjectContextService', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'tasks', component: RouteTarget },
          { path: 'wiki', component: RouteTarget },
          { path: 'overview', component: RouteTarget },
          { path: 'projects/:projectId/tasks', component: RouteTarget },
          { path: 'projects/:projectId/wiki', component: RouteTarget },
          { path: 'projects/:projectId/overview', component: RouteTarget },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('qualifies linked routes and retains task filters independently by project ID', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/projects/prj_child/tasks');
    const context = TestBed.inject(ProjectContextService);

    expect(context.selectedProjectId()).toBe('prj_child');
    expect(context.readOnly()).toBe(true);
    expect(context.apiUrl('/board')).toBe('/api/v1/projects/prj_child/board');
    expect(context.settingsRoot()).toBe('/projects/prj_child/tasks/settings');
    expect(context.taskUrl('GAME-0001', true)).toBe('/projects/prj_child/tasks/dialog/GAME-0001');
    expect(context.wikiUrl('guide/start')).toBe('/projects/prj_child/wiki/guide/start');
    expect(context.wikiCreateUrl()).toBe('/projects/prj_child/wiki/new');
    expect(context.wikiEditUrl('guide/start')).toBe('/projects/prj_child/wiki/edit/guide/start');
    expect(context.wikiMetadataUrl('guide/start')).toBe(
      '/projects/prj_child/wiki/meta/guide/start',
    );

    context.rememberTaskFilters({ track: 'GAME', state: 'todo', includeDelivered: true });
    expect(context.modeUrl('tasks')).toBe(
      '/projects/prj_child/tasks?track=GAME&state=todo&includeDelivered=true',
    );
    expect(context.modeUrl('wiki')).toBe('/projects/prj_child/wiki');
    expect(context.projectModeUrl('prj_child')).toBe(
      '/projects/prj_child/tasks?track=GAME&state=todo&includeDelivered=true',
    );

    context.enableFamilyMetadata();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/projects/prj_child/project').flush({
      projectId: 'prj_child',
      name: 'Child',
      accent: 'teal',
      relationship: 'child',
      readOnly: true,
      revision: 'child-revision',
    });
    http.expectOne('/api/v1/project/links').flush({
      activeProjectId: 'prj_active',
      members: [
        {
          projectId: 'prj_active',
          name: 'Active',
          alias: null,
          relationship: 'current',
          status: 'resolved',
          source: 'current',
          readable: true,
          writeTrusted: true,
        },
        {
          projectId: 'prj_child',
          name: 'Child',
          alias: 'child',
          relationship: 'child',
          status: 'resolved',
          source: 'manifest',
          readable: true,
          writeTrusted: false,
        },
      ],
      warnings: [],
    });
    await TestBed.tick();

    expect(context.projectModeUrl('prj_active')).toBe('/tasks');
    expect(context.displayProject()?.name).toBe('Child');
    expect(context.readOnly()).toBe(true);
    context.family.update((family) => ({
      ...family!,
      members: family!.members.map((member) =>
        member.projectId === 'prj_child' ? { ...member, writeTrusted: true } : member,
      ),
    }));
    expect(context.readOnly()).toBe(false);
  });

  it('keeps current-project settings on the ordinary task root', async () => {
    await TestBed.inject(Router).navigateByUrl('/tasks');
    expect(TestBed.inject(ProjectContextService).settingsRoot()).toBe('/tasks/settings');
  });

  it('treats Overview as a project-scoped mode without carrying task filters', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/projects/prj_child/overview');
    const context = TestBed.inject(ProjectContextService);

    expect(context.mode()).toBe('overview');
    expect(context.overviewRoot()).toBe('/projects/prj_child/overview');
    expect(context.modeUrl('overview')).toBe('/projects/prj_child/overview');
    expect(context.projectModeUrl('prj_other')).toBe('/projects/prj_other/overview');
  });

  it('restores task visibility after static task and wiki navigation', async () => {
    TestBed.overrideProvider(StaticModeService, { useValue: { enabled: true } });
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/tasks?state=todo&includeDelivered=true');
    const context = TestBed.inject(ProjectContextService);

    expect(context.taskFilters()).toEqual({ state: 'todo', includeDelivered: true });
    context.rememberTaskFilters(context.taskFilters());
    await router.navigateByUrl('/wiki');

    expect(context.modeUrl('tasks')).toBe('/tasks?state=todo&includeDelivered=true');
  });
});

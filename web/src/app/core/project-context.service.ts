import { httpResource } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, UrlTree } from '@angular/router';
import { filter, map } from 'rxjs';

import { StaticModeService } from '../static/static-mode.service';
import { AccentService } from './accent.service';

export interface ProjectContextResponse {
  projectId: string;
  name: string;
  accent: string;
  relationship: string;
  readOnly: boolean;
  revision: string;
}

export interface LinkedProjectMember {
  projectId: string;
  name: string;
  alias: string | null;
  relationship: string;
  status: string;
  source: string;
  readable: boolean;
  writeTrusted: boolean;
}

export interface LinkedProjectWarning {
  code: string;
  message: string;
  declaringProjectId: string;
  targetProjectId: string;
  alias: string | null;
  status: string;
  repairCommand: string | null;
}

export interface LinkedProjectFamily {
  activeProjectId: string;
  members: LinkedProjectMember[];
  warnings: LinkedProjectWarning[];
}

@Injectable({ providedIn: 'root' })
export class ProjectContextService {
  private readonly router = inject(Router, { optional: true });
  private readonly staticMode = inject(StaticModeService);
  private readonly accent = inject(AccentService);
  private readonly projectMetadataEnabled = signal(false);
  private readonly familyMetadataEnabled = signal(false);
  private readonly rememberedTaskFilters = signal<Record<string, string | null>>({});
  private readonly currentUrl = this.router
    ? toSignal(
        this.router.events.pipe(
          filter((event): event is NavigationEnd => event instanceof NavigationEnd),
          map(() => this.router!.url),
        ),
        {
          initialValue: this.router.currentNavigation()?.finalUrl?.toString() ?? this.router.url,
        },
      )
    : signal('/tasks');

  readonly selectedProjectId = computed(() => {
    const segments = this.segments();
    return segments[0] === 'projects' && segments[1] ? segments[1] : null;
  });
  readonly apiPrefix = computed(() => {
    const projectId = this.selectedProjectId();
    return projectId ? `/api/v1/projects/${encodeURIComponent(projectId)}` : '/api/v1';
  });
  readonly readOnly = computed(() => !!this.selectedProjectId() || this.staticMode.enabled);
  readonly mode = computed<'tasks' | 'wiki'>(() => {
    const segments = this.segments();
    const mode = segments[0] === 'projects' ? segments[2] : segments[0];
    return mode === 'wiki' ? 'wiki' : 'tasks';
  });
  readonly tasksRoot = computed(() => this.routeRoot('tasks'));
  readonly wikiRoot = computed(() => this.routeRoot('wiki'));
  readonly storageProjectId = computed(() => this.selectedProjectId() ?? 'current');

  readonly family = httpResource<LinkedProjectFamily>(() =>
    this.staticMode.enabled || !this.familyMetadataEnabled() ? undefined : '/api/v1/project/links',
  );
  readonly project = httpResource<ProjectContextResponse>(() =>
    this.projectMetadataEnabled() ? `${this.apiPrefix()}/project` : undefined,
  );
  readonly selectedMember = computed(() => {
    const family = this.family.hasValue() ? this.family.value() : null;
    const selected = this.selectedProjectId() ?? family?.activeProjectId;
    return family?.members.find((member) => member.projectId === selected) ?? null;
  });
  readonly displayProject = computed(() => {
    const project = this.project.hasValue() ? this.project.value() : null;
    if (project) return { projectId: project.projectId, name: project.name };
    const member = this.selectedMember();
    if (member) return { projectId: member.projectId, name: member.name };
    const selectedProjectId = this.selectedProjectId();
    return selectedProjectId && this.family.hasValue()
      ? { projectId: selectedProjectId, name: 'Unknown project' }
      : null;
  });

  constructor() {
    effect(() => {
      if (this.project.hasValue()) this.accent.applyProjectPreference(this.project.value().accent);
    });
  }

  enableProjectMetadata(): void {
    this.projectMetadataEnabled.set(true);
  }

  enableFamilyMetadata(): void {
    this.projectMetadataEnabled.set(true);
    this.familyMetadataEnabled.set(true);
  }

  apiUrl(path: string): string {
    return `${this.apiPrefix()}${path.startsWith('/') ? path : `/${path}`}`;
  }

  taskUrl(taskId?: string, dialog = false): string {
    const root = this.tasksRoot();
    if (!taskId) return root;
    return `${root}/${dialog ? 'dialog/' : ''}${encodeURIComponent(taskId)}`;
  }

  wikiUrl(path?: string): string {
    const root = this.wikiRoot();
    if (!path) return root;
    return `${root}/${path
      .split('/')
      .map((segment) => encodeURIComponent(segment))
      .join('/')}`;
  }

  projectModeUrl(projectId: string, mode: 'tasks' | 'wiki' = this.mode()): string {
    const activeProjectId = this.family.hasValue() ? this.family.value().activeProjectId : null;
    const root =
      projectId === activeProjectId
        ? `/${mode}`
        : `/projects/${encodeURIComponent(projectId)}/${mode}`;
    if (mode !== 'tasks') return root;
    try {
      const storageProjectId = projectId === activeProjectId ? 'current' : projectId;
      const query = this.readTaskFilters(storageProjectId);
      return query ? `${root}?${query}` : root;
    } catch {
      return root;
    }
  }

  modeUrl(mode: 'tasks' | 'wiki'): string {
    const root = this.routeRoot(mode);
    if (mode !== 'tasks' || this.staticMode.enabled) return root;
    try {
      const query = this.readTaskFilters(this.storageProjectId());
      return query ? `${root}?${query}` : root;
    } catch {
      return root;
    }
  }

  modeLink(mode: 'tasks' | 'wiki'): string | UrlTree {
    const url = this.modeUrl(mode);
    return url.includes('?') ? (this.router?.parseUrl(url) ?? url) : url;
  }

  projectModeLink(projectId: string, mode: 'tasks' | 'wiki' = this.mode()): string | UrlTree {
    const url = this.projectModeUrl(projectId, mode);
    return url.includes('?') ? (this.router?.parseUrl(url) ?? url) : url;
  }

  rememberTaskFilters(filters: Record<string, string | undefined>): void {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(filters)) if (value) params.set(key, value);
    try {
      const projectId = this.storageProjectId();
      const value = params.toString();
      this.rememberedTaskFilters.update((current) => ({
        ...current,
        [projectId]: value || null,
      }));
      if (value) sessionStorage.setItem(this.taskFilterKey(projectId), value);
      else sessionStorage.removeItem(this.taskFilterKey(projectId));
    } catch {
      // Project switching remains usable when session storage is unavailable.
    }
  }

  private routeRoot(mode: 'tasks' | 'wiki'): string {
    const projectId = this.selectedProjectId();
    return projectId ? `/projects/${encodeURIComponent(projectId)}/${mode}` : `/${mode}`;
  }

  private taskFilterKey(projectId: string): string {
    return `pm.task-filters.v1.${encodeURIComponent(projectId)}`;
  }

  private readTaskFilters(projectId: string): string | null {
    const remembered = this.rememberedTaskFilters();
    if (Object.hasOwn(remembered, projectId)) return remembered[projectId] ?? null;
    return sessionStorage.getItem(this.taskFilterKey(projectId));
  }

  private segments(): string[] {
    return (
      this.router
        ?.parseUrl(this.currentUrl())
        .root.children['primary']?.segments.map((segment) => segment.path) ?? []
    );
  }
}

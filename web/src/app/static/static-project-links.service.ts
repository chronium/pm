import { inject, Injectable } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';

import { StaticModeService } from './static-mode.service';
import { StaticSnapshotStore } from './static-snapshot.interceptor';

export type StaticProjectLinkResolution =
  | { kind: 'available'; href: string; local: boolean }
  | { kind: 'unavailable'; reason: string }
  | { kind: 'not-project-link' };

interface CanonicalProjectReference {
  projectId: string;
  resource: 'task' | 'wiki';
  value: string;
}

@Injectable({ providedIn: 'root' })
export class StaticProjectLinksService {
  private readonly mode = inject(StaticModeService);
  private readonly store = inject(StaticSnapshotStore);
  private readonly snapshot = toSignal(this.mode.enabled ? this.store.snapshot : of(null), {
    initialValue: null,
  });

  resolve(reference: string): StaticProjectLinkResolution {
    if (!this.mode.enabled) return { kind: 'not-project-link' };
    const parsed = parseCanonicalProjectReference(reference);
    if (!parsed) {
      return reference.startsWith('pm://')
        ? { kind: 'unavailable', reason: 'This project reference is malformed.' }
        : { kind: 'not-project-link' };
    }

    const snapshot = this.snapshot();
    if (!snapshot)
      return { kind: 'unavailable', reason: 'Linked project information is still loading.' };
    const route = staticRoute(parsed);
    if (snapshot.projectId === parsed.projectId)
      return { kind: 'available', href: `#${route}`, local: true };

    const project = snapshot.linkedProjects.find(
      (candidate) => candidate.projectId === parsed.projectId,
    );
    if (!project)
      return { kind: 'unavailable', reason: `Project ${parsed.projectId} is not linked.` };
    if (!project.publicSiteUrl)
      return {
        kind: 'unavailable',
        reason: `${project.name} does not publish a static site URL.`,
      };

    try {
      const target = new URL(project.publicSiteUrl);
      if (target.protocol !== 'http:' && target.protocol !== 'https:') throw new Error();
      target.hash = route;
      return { kind: 'available', href: target.toString(), local: false };
    } catch {
      return { kind: 'unavailable', reason: `${project.name} has an invalid static site URL.` };
    }
  }
}

export function parseCanonicalProjectReference(
  reference: string,
): CanonicalProjectReference | null {
  if (/\s/.test(reference)) return null;
  const match = /^pm:\/\/project\/([^/?#]+)\/(task|wiki)\/(.+)$/.exec(reference);
  if (!match || match[3]!.includes('?') || match[3]!.includes('#')) return null;
  try {
    const projectId = decodeURIComponent(match[1]!);
    const resource = match[2] as CanonicalProjectReference['resource'];
    const segments = match[3]!.split('/').map((segment) => decodeURIComponent(segment));
    if (
      !projectId ||
      projectId.includes('/') ||
      projectId.includes('\\') ||
      segments.some(
        (segment) => !segment || segment === '.' || segment === '..' || /[/\\]/.test(segment),
      ) ||
      (resource === 'task' && segments.length !== 1)
    )
      return null;
    return { projectId, resource, value: segments.join('/') };
  } catch {
    return null;
  }
}

function staticRoute(reference: CanonicalProjectReference): string {
  const value = reference.value
    .split('/')
    .map((segment) => encodeURIComponent(segment))
    .join('/');
  return reference.resource === 'task' ? `/tasks/${value}` : `/wiki/${value}`;
}

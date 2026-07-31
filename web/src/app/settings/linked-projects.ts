import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  type LinkedProjectFamily,
  type LinkedProjectMember,
  ProjectContextService,
} from '../core/project-context.service';

@Component({
  selector: 'pm-linked-projects',
  templateUrl: './linked-projects.html',
  styleUrl: './linked-projects.css',
})
export class LinkedProjects {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  protected readonly context = inject(ProjectContextService);
  protected readonly pendingProjectId = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.context.enableLinkedProjectFamily();
  }

  protected linkedMembers(): readonly LinkedProjectMember[] {
    return this.context.family.hasValue()
      ? this.context.family.value().members.filter((member) => member.relationship !== 'current')
      : [];
  }

  protected async setTrust(member: LinkedProjectMember, trusted: boolean): Promise<void> {
    const verb = trusted ? 'grant' : 'revoke';
    const confirmed = this.document.defaultView?.confirm(
      `${trusted ? 'Allow' : 'Stop'} local writes to ${member.name}?`,
    );
    if (!confirmed || this.pendingProjectId()) return;

    this.pendingProjectId.set(member.projectId);
    this.error.set(null);
    const url = `/api/v1/project/links/${encodeURIComponent(member.projectId)}/write-trust`;
    try {
      const options = { headers: { 'X-PM-Client': 'angular-web' } };
      const family = trusted
        ? await firstValueFrom(this.http.post<LinkedProjectFamily>(url, {}, options))
        : await firstValueFrom(this.http.delete<LinkedProjectFamily>(url, options));
      this.context.family.set(family);
    } catch (error) {
      this.error.set(this.message(error, `Could not ${verb} linked-project write trust.`));
    } finally {
      this.pendingProjectId.set(null);
    }
  }

  private message(error: unknown, fallback: string): string {
    if (!(error instanceof HttpErrorResponse)) return fallback;
    const problem = error.error as { detail?: unknown; title?: unknown } | null;
    if (typeof problem?.detail === 'string' && problem.detail.trim()) return problem.detail;
    if (typeof problem?.title === 'string' && problem.title.trim()) return problem.title;
    return fallback;
  }
}

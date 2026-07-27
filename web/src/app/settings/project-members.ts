import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';

import type { components } from '../api/generated/pm-api';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../ui/state/state';

export type LocalIdentity = components['schemas']['LocalIdentityResponse'];
export type ProjectMember = components['schemas']['ProjectMemberResponse'];
export type ProjectMembersResponse = components['schemas']['ProjectMembersResponse'];
export type ProjectInvitation = components['schemas']['ProjectInvitationResponse'];
export type ProjectInvitationsResponse = components['schemas']['ProjectInvitationsResponse'];
export type CreatedProjectInvitation = components['schemas']['CreatedProjectInvitationResponse'];

type DialogMode = 'invite' | 'secret' | 'join' | 'role' | null;
type Confirmation =
  { kind: 'revoke'; invitation: ProjectInvitation } | { kind: 'remove'; member: ProjectMember };

@Component({
  selector: 'pm-project-members',
  imports: [PmConfirmDialog, PmErrorState, PmLoadingState],
  templateUrl: './project-members.html',
  styleUrl: './project-members.css',
})
export class ProjectMembers {
  private readonly http = inject(HttpClient);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('membershipDialog');

  protected readonly identity = signal<LocalIdentity | null>(null);
  protected readonly membership = signal<ProjectMembersResponse | null>(null);
  protected readonly invitations = signal<ProjectInvitation[]>([]);
  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly mode = signal<DialogMode>(null);
  protected readonly invitationRole = signal<'user' | 'admin'>('user');
  protected readonly invitationToken = signal('');
  protected readonly oneTimeSecret = signal<string | null>(null);
  protected readonly selectedMember = signal<ProjectMember | null>(null);
  protected readonly selectedRole = signal<'user' | 'admin'>('user');
  protected readonly confirmation = signal<Confirmation | null>(null);

  protected readonly admin = () => this.membership()?.currentRole === 'admin';

  constructor() {
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.mode() && !dialog.open) {
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');
      } else if (!this.mode() && dialog.open) {
        if (typeof dialog.close === 'function') dialog.close();
        else dialog.removeAttribute('open');
      }
    });
    this.reload();
  }

  protected reload(): void {
    this.error.set(null);
    this.refreshing.set(!!this.membership());
    this.http.get<LocalIdentity>('/api/v1/project/identity').subscribe({
      next: (identity) => this.identity.set(identity),
      error: (error) =>
        this.error.set(this.message(error, 'The local identity could not be loaded.')),
    });
    this.http.get<ProjectMembersResponse>('/api/v1/project/members').subscribe({
      next: (membership) => {
        this.membership.set(membership);
        this.loading.set(false);
        this.refreshing.set(false);
        if (membership.currentRole === 'admin') this.loadInvitations();
        else this.invitations.set([]);
      },
      error: (error) => {
        this.loading.set(false);
        this.refreshing.set(false);
        this.error.set(this.message(error, 'Project membership could not be loaded.'));
      },
    });
  }

  protected openInvite(): void {
    this.invitationRole.set('user');
    this.dialogError.set(null);
    this.mode.set('invite');
  }

  protected openJoin(): void {
    this.invitationToken.set('');
    this.dialogError.set(null);
    this.mode.set('join');
  }

  protected openRole(member: ProjectMember): void {
    this.selectedMember.set(member);
    this.selectedRole.set(member.role === 'admin' ? 'admin' : 'user');
    this.dialogError.set(null);
    this.mode.set('role');
  }

  protected closeDialog(): void {
    if (this.pending()) return;
    this.invitationToken.set('');
    this.oneTimeSecret.set(null);
    this.selectedMember.set(null);
    this.dialogError.set(null);
    this.mode.set(null);
  }

  protected createInvitation(event: Event): void {
    event.preventDefault();
    if (this.pending()) return;
    this.pending.set(true);
    this.dialogError.set(null);
    this.http
      .post<CreatedProjectInvitation>(
        '/api/v1/project/invitations',
        { role: this.invitationRole() },
        { headers: { 'X-PM-Client': 'angular-web' } },
      )
      .subscribe({
        next: (created) => {
          this.pending.set(false);
          this.oneTimeSecret.set(created.token);
          this.invitations.update((items) => [created.invitation, ...items]);
          this.mode.set('secret');
          queueMicrotask(() => this.ensureDialogOpen('secret'));
        },
        error: (error) => {
          this.pending.set(false);
          this.dialogError.set(this.message(error, 'The invitation could not be created.'));
        },
      });
  }

  protected acceptInvitation(event: Event): void {
    event.preventDefault();
    const token = this.invitationToken().trim();
    if (!token || this.pending()) return;
    this.pending.set(true);
    this.dialogError.set(null);
    this.http
      .post<ProjectMember>(
        '/api/v1/project/invitations/accept',
        { token },
        { headers: { 'X-PM-Client': 'angular-web' } },
      )
      .subscribe({
        next: () => {
          this.pending.set(false);
          this.closeDialog();
          this.reload();
        },
        error: (error) => {
          this.pending.set(false);
          this.dialogError.set(this.message(error, 'The invitation could not be accepted.'));
        },
      });
  }

  protected updateRole(event: Event): void {
    event.preventDefault();
    const member = this.selectedMember();
    if (!member || this.pending()) return;
    this.pending.set(true);
    this.dialogError.set(null);
    this.http
      .patch<ProjectMember>(
        `/api/v1/project/members/${encodeURIComponent(member.userId)}`,
        { role: this.selectedRole() },
        { headers: { 'X-PM-Client': 'angular-web' } },
      )
      .subscribe({
        next: () => {
          this.pending.set(false);
          this.closeDialog();
          this.reload();
        },
        error: (error) => {
          this.pending.set(false);
          this.dialogError.set(this.message(error, 'The role could not be updated.'));
          this.reloadMembersAfterConflict(error);
        },
      });
  }

  protected confirmMutation(): void {
    const confirmation = this.confirmation();
    if (!confirmation || this.pending()) return;
    this.pending.set(true);
    const url =
      confirmation.kind === 'revoke'
        ? `/api/v1/project/invitations/${encodeURIComponent(confirmation.invitation.invitationId)}`
        : `/api/v1/project/members/${encodeURIComponent(confirmation.member.userId)}`;
    this.http.delete(url, { headers: { 'X-PM-Client': 'angular-web' } }).subscribe({
      next: () => {
        this.pending.set(false);
        this.confirmation.set(null);
        this.reload();
      },
      error: (error) => {
        this.pending.set(false);
        this.confirmation.set(null);
        this.error.set(this.message(error, 'The membership change could not be completed.'));
        this.reloadMembersAfterConflict(error);
      },
    });
  }

  protected copySecret(): void {
    const secret = this.oneTimeSecret();
    if (secret) void navigator.clipboard?.writeText(secret);
  }

  protected expires(value: string): string {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
      new Date(value),
    );
  }

  protected confirmationHeading(): string {
    return this.confirmation()?.kind === 'revoke' ? 'Revoke invitation?' : 'Remove project member?';
  }

  protected confirmationMessage(): string {
    const confirmation = this.confirmation();
    if (!confirmation) return '';
    return confirmation.kind === 'revoke'
      ? `Revoke invitation ${confirmation.invitation.invitationId}? It can no longer be accepted.`
      : `Remove ${confirmation.member.displayName} (${confirmation.member.userId}) from this project?`;
  }

  private loadInvitations(): void {
    this.http.get<ProjectInvitationsResponse>('/api/v1/project/invitations').subscribe({
      next: (response) => this.invitations.set(response.invitations),
      error: (error) =>
        this.error.set(this.message(error, 'Pending invitations could not be loaded.')),
    });
  }

  private ensureDialogOpen(expectedMode: DialogMode): void {
    const dialog = this.dialog().nativeElement;
    if (this.mode() !== expectedMode || dialog.open) return;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
  }

  private reloadMembersAfterConflict(error: unknown): void {
    if (error instanceof HttpErrorResponse && [404, 409, 412].includes(error.status)) this.reload();
  }

  private message(error: unknown, fallback: string): string {
    if (!(error instanceof HttpErrorResponse)) return fallback;
    const problem = error.error as { detail?: string; title?: string } | null;
    return (
      problem?.detail?.trim() ||
      problem?.title?.trim() ||
      (error.status === 0
        ? 'The membership service is offline. Local project work is still available.'
        : `${fallback} (${error.status}).`)
    );
  }
}

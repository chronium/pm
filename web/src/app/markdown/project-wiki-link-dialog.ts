import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import type { components } from '../api/generated/pm-api';
import { ProjectContextService } from '../core/project-context.service';
import { formatCanonicalProjectReference } from '../core/project-links.service';
import { PmFormField } from '../ui/form-field/form-field';

type WikiPageSummary = components['schemas']['WikiPageSummaryResponse'];

@Component({
  selector: 'pm-project-wiki-link-dialog',
  imports: [PmFormField],
  templateUrl: './project-wiki-link-dialog.html',
  styleUrl: './project-wiki-link-dialog.css',
})
export class ProjectWikiLinkDialog {
  readonly open = input(false);
  readonly initialLabel = input('');
  readonly inserted = output<string>();
  readonly dismissed = output<void>();

  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly projectContext = inject(ProjectContextService);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');
  private loadedProjectId: string | null = null;
  private wasOpen = false;

  protected readonly selectedProjectId = signal('');
  protected readonly selectedPage = signal<WikiPageSummary | null>(null);
  protected readonly pages = signal<WikiPageSummary[]>([]);
  protected readonly filter = signal('');
  protected readonly label = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly members = computed(() =>
    this.projectContext.family.hasValue()
      ? this.projectContext.family.value().members.filter((member) => member.readable)
      : [],
  );
  protected readonly filteredPages = computed(() => {
    const filter = this.filter().trim().toLowerCase();
    return filter
      ? this.pages().filter(
          (page) =>
            page.title.toLowerCase().includes(filter) || page.path.toLowerCase().includes(filter),
        )
      : this.pages();
  });

  constructor() {
    this.projectContext.enableLinkedProjectFamily();
    effect(() => {
      const open = this.open();
      const dialog = this.dialog()?.nativeElement;
      if (!open) {
        this.wasOpen = false;
        if (dialog?.open) {
          if (typeof dialog.close === 'function') dialog.close();
          else dialog.removeAttribute('open');
        }
        return;
      }
      if (!this.wasOpen) {
        this.wasOpen = true;
        this.label.set(this.initialLabel());
        this.filter.set('');
        this.selectedPage.set(null);
      }
      if (dialog && !dialog.open) {
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');
      }
      const members = this.members();
      if (!members.length) return;
      const selected = this.selectedProjectId();
      const fallback =
        members.find(
          (member) => member.projectId === this.projectContext.selectedMember()?.projectId,
        ) ?? members[0]!;
      if (!members.some((member) => member.projectId === selected)) {
        this.selectedProjectId.set(fallback.projectId);
        return;
      }
      if (this.loadedProjectId !== selected) this.loadPages(selected);
    });
  }

  protected selectProject(event: Event): void {
    this.selectedProjectId.set((event.target as HTMLSelectElement).value);
    this.loadedProjectId = null;
    this.pages.set([]);
    this.selectedPage.set(null);
    this.error.set(null);
  }

  protected selectPage(page: WikiPageSummary): void {
    this.selectedPage.set(page);
    if (!this.label().trim()) this.label.set(page.title);
  }

  protected insert(): void {
    const page = this.selectedPage();
    const projectId = this.selectedProjectId();
    if (!page || !projectId) return;
    const label = escapeMarkdownLabel(this.label().trim() || page.title);
    const reference = formatCanonicalProjectReference({
      projectId,
      resource: 'wiki',
      value: page.path,
    });
    this.inserted.emit(`[${label}](${reference})`);
  }

  protected dismiss(): void {
    this.dismissed.emit();
  }

  protected cancel(event: Event): void {
    event.preventDefault();
    this.dismiss();
  }

  private loadPages(projectId: string): void {
    this.loadedProjectId = projectId;
    this.loading.set(true);
    this.error.set(null);
    const family = this.projectContext.family.hasValue()
      ? this.projectContext.family.value()
      : null;
    if (!family) return;
    const activeProjectId = family.activeProjectId;
    const endpoint =
      projectId === activeProjectId
        ? '/api/v1/wiki/pages'
        : `/api/v1/projects/${encodeURIComponent(projectId)}/wiki/pages`;
    this.http
      .get<WikiPageSummary[]>(endpoint)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (pages) => {
          if (this.selectedProjectId() !== projectId) return;
          this.pages.set(pages);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          if (this.selectedProjectId() !== projectId) return;
          this.loading.set(false);
          this.error.set(
            error instanceof HttpErrorResponse && error.status === 0
              ? 'The wiki API could not be reached.'
              : 'Wiki pages could not be loaded.',
          );
        },
      });
  }
}

function escapeMarkdownLabel(label: string): string {
  return label.replace(/[\\[\]]/g, '\\$&');
}

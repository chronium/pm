import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';

@Injectable({ providedIn: 'root' })
export class TaskNavigationService {
  readonly remainingCount = signal<number | null>(null);
  readonly refreshRequest = signal(0);
  private origin: HTMLElement | null = null;
  private readonly mobileQuery = '(max-width: 767px)';

  setRemainingCount(count: number): void {
    this.remainingCount.set(count);
  }

  requestNavigationRefresh(): void {
    this.refreshRequest.update((value) => value + 1);
  }

  captureOrigin(element: EventTarget | null): void {
    this.origin = element instanceof HTMLElement ? element : null;
  }

  shouldOpenDialog(event: MouseEvent): boolean {
    return (
      event.button === 0 &&
      !event.metaKey &&
      !event.ctrlKey &&
      !event.shiftKey &&
      !event.altKey &&
      !this.isMobile()
    );
  }

  isMobile(): boolean {
    return typeof window.matchMedia === 'function' && window.matchMedia(this.mobileQuery).matches;
  }

  navigateToTask(router: Router, taskId: string, recommendationReason?: string): Promise<boolean> {
    const returnUrl = router.url;
    const target = this.isMobile() ? ['/tasks', taskId] : ['/tasks', 'dialog', taskId];
    return router.navigate(target, {
      queryParams: router.parseUrl(returnUrl).queryParams,
      state: { returnUrl, ...(recommendationReason ? { recommendationReason } : {}) },
    });
  }

  openDialog(
    event: MouseEvent,
    router: Router,
    taskId: string | 'new',
  ): Promise<boolean> | undefined {
    this.captureOrigin(event.currentTarget);
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
      return undefined;
    event.preventDefault();
    const returnUrl = router.url.startsWith('/tasks/dialog/') ? this.returnUrl(router) : router.url;
    const target = this.isMobile() ? ['/tasks', taskId] : ['/tasks', 'dialog', taskId];
    return router.navigate(target, {
      queryParams: router.parseUrl(returnUrl).queryParams,
      state: { returnUrl },
    });
  }

  canonicalHref(router: Router, taskId: string | 'new'): string {
    return router.serializeUrl(
      router.createUrlTree(['/tasks', taskId], {
        queryParams: router.parseUrl(router.url).queryParams,
      }),
    );
  }

  returnUrl(router: Router): string {
    const candidate: unknown = history.state?.returnUrl;
    return typeof candidate === 'string' && this.validReturnUrl(router, candidate)
      ? candidate
      : this.scopedBoardUrl(router);
  }

  returnState(router: Router): { returnUrl: string; recommendationReason?: string } {
    const recommendationReason = this.recommendationReason();
    return {
      returnUrl: this.returnUrl(router),
      ...(recommendationReason ? { recommendationReason } : {}),
    };
  }

  recommendationReason(): string | null {
    const candidate: unknown = history.state?.recommendationReason;
    return typeof candidate === 'string' && candidate.trim() ? candidate : null;
  }

  restoreFocus(): void {
    const origin = this.origin;
    this.origin = null;
    setTimeout(() => origin?.isConnected && origin.focus());
  }

  private validReturnUrl(router: Router, candidate: string): boolean {
    if (!candidate.startsWith('/') || candidate.startsWith('//')) return false;
    const segments =
      router.parseUrl(candidate).root.children['primary']?.segments.map((item) => item.path) ?? [];
    return segments.length === 1 && segments[0] === 'tasks';
  }

  private scopedBoardUrl(router: Router): string {
    const current = router.parseUrl(router.url);
    const queryParams = Object.fromEntries(
      ['track', 'milestone', 'state']
        .map((key) => [key, current.queryParams[key]])
        .filter(
          (entry): entry is [string, string] => typeof entry[1] === 'string' && !!entry[1].trim(),
        ),
    );
    return router.serializeUrl(router.createUrlTree(['/tasks'], { queryParams }));
  }
}

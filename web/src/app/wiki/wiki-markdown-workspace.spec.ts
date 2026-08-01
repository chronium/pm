import { Component, Injector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { disabled, FormField, form } from '@angular/forms/signals';

import { MarkdownEditor } from '../markdown/markdown-editor';
import { WikiMarkdownWorkspace } from './wiki-markdown-workspace';

Object.defineProperty(Range.prototype, 'getBoundingClientRect', {
  configurable: true,
  value: () => new DOMRect(),
});
Object.defineProperty(Range.prototype, 'getClientRects', {
  configurable: true,
  value: () => [],
});

@Component({
  imports: [FormField, WikiMarkdownWorkspace],
  template: `<pm-wiki-markdown-workspace pmControl [formField]="pageForm.body" />`,
})
class WorkspaceHost {
  private readonly injector = TestBed.inject(Injector);
  readonly disabled = signal(false);
  readonly model = signal({ body: '' });
  readonly pageForm = form(this.model, (page) => disabled(page.body, this.disabled), {
    injector: this.injector,
  });
}

describe('WikiMarkdownWorkspace', () => {
  it('integrates with signal forms and updates its sanitized preview after a debounce', async () => {
    const fixture = TestBed.createComponent(WorkspaceHost);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    fixture.detectChanges();
    const workspace = fixture.debugElement.query(By.directive(WikiMarkdownWorkspace))
      .componentInstance as WikiMarkdownWorkspace;

    vi.useFakeTimers();
    try {
      workspace.value.set('**Draft** <script>alert(1)</script>');
      fixture.detectChanges();
      expect(fixture.componentInstance.pageForm().dirty()).toBe(true);
      expect(element.querySelector('strong')).toBeNull();

      vi.advanceTimersByTime(119);
      fixture.detectChanges();
      expect(element.querySelector('strong')).toBeNull();

      vi.advanceTimersByTime(1);
      fixture.detectChanges();
      expect(element.querySelector('strong')?.textContent).toBe('Draft');
      expect(element.querySelector('script')).toBeNull();
    } finally {
      fixture.destroy();
      vi.useRealTimers();
    }
  });

  it('uses external-preview controls and propagates its disabled state', async () => {
    const fixture = TestBed.createComponent(WorkspaceHost);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const toolbarLabels = [
      ...element.querySelectorAll<HTMLButtonElement>('.editor-toolbar button'),
    ].map((button) => button.getAttribute('aria-label') ?? button.title);
    expect(toolbarLabels.some((label) => /preview/i.test(label))).toBe(false);
    expect(toolbarLabels.some((label) => /side.by.side/i.test(label))).toBe(false);
    expect(toolbarLabels.some((label) => /full.?screen/i.test(label))).toBe(false);
    expect(toolbarLabels.some((label) => /bold/i.test(label))).toBe(true);
    expect(toolbarLabels.some((label) => /project wiki link/i.test(label))).toBe(true);

    fixture.componentInstance.disabled.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(
      [...element.querySelectorAll<HTMLButtonElement>('.editor-toolbar button')].every(
        (button) => button.disabled,
      ),
    ).toBe(true);
  });

  it('keeps the default EasyMDE preview controls for task-style editors', async () => {
    const fixture = TestBed.createComponent(MarkdownEditor);
    fixture.componentRef.setInput('value', 'Task body');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const labels = [...element.querySelectorAll<HTMLButtonElement>('.editor-toolbar button')].map(
      (button) => button.getAttribute('aria-label') ?? button.title,
    );
    expect(labels.some((label) => /preview/i.test(label))).toBe(true);
    expect(labels.some((label) => /side.by.side/i.test(label))).toBe(true);
    expect(labels.some((label) => /full.?screen/i.test(label))).toBe(true);
    fixture.destroy();
  });

  it('implements roving keyboard focus for the mobile tab interface', async () => {
    const fixture = TestBed.createComponent(WorkspaceHost);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const tabs = element.querySelectorAll<HTMLButtonElement>('[role="tab"]');
    tabs[0]!.focus();
    tabs[0]!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(tabs[1]!.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs[1]);
    expect(element.querySelector('.wiki-workspace-preview')?.isConnected).toBe(true);
  });
});

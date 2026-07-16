import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TopBarSearch, type TopBarSearchOption } from './top-bar-search';

@Component({
  imports: [TopBarSearch],
  template: `
    <pm-top-bar-search
      ariaLabel="Search examples"
      listboxLabel="Example results"
      placeholder="Search examples"
      emptyMessage="Nothing found."
      [(query)]="query"
      [options]="options()"
      [loading]="loading()"
      [error]="error()"
      (optionSelected)="selected.set($event.id)"
    />
  `,
})
class Host {
  readonly query = signal('');
  readonly options = signal<TopBarSearchOption[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly selected = signal('');
}

describe('TopBarSearch', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;
  let element: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    element = fixture.nativeElement;
    fixture.detectChanges();
  });

  function focus(): HTMLInputElement {
    const input = element.querySelector('input')!;
    input.focus();
    input.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    return input;
  }

  it('supports keyboard navigation and selection with combobox semantics', () => {
    host.options.set([
      { id: 'first', primary: 'First' },
      { id: 'second', primary: 'Second' },
    ]);
    const input = focus();
    expect(input.getAttribute('aria-expanded')).toBe('true');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    fixture.detectChanges();
    expect(input.getAttribute('aria-activedescendant')).toContain('option-1');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(host.selected()).toBe('second');
  });

  it('presents loading, error, and empty states', () => {
    host.query.set('term');
    host.loading.set(true);
    focus();
    expect(element.textContent).toContain('Searching…');
    host.loading.set(false);
    host.error.set('Search failed.');
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Search failed.');
    host.error.set(null);
    fixture.detectChanges();
    expect(element.textContent).toContain('Nothing found.');
  });

  it('closes on Escape or outside blur and expands from the mobile trigger', async () => {
    host.options.set([{ id: 'first', primary: 'First' }]);
    const mobile = element.querySelector('.mobile-search-button') as HTMLButtonElement;
    mobile.click();
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    const input = element.querySelector('input')!;
    expect(document.activeElement).toBe(input);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(input.getAttribute('aria-expanded')).toBe('false');

    focus();
    input.blur();
    input.dispatchEvent(new Event('blur'));
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    expect(input.getAttribute('aria-expanded')).toBe('false');
  });
});

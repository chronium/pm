import { TestBed } from '@angular/core/testing';

import { eventLogEntries } from './agent-run-events';
import { AgentRunOutput } from './agent-run-output';
import { runEvents } from './agent-runs.fixtures';

describe('AgentRunOutput', () => {
  beforeEach(async () => {
    Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
      configurable: true,
      value: vi.fn(),
    });
    await TestBed.configureTestingModule({ imports: [AgentRunOutput] }).compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  function render() {
    const fixture = TestBed.createComponent(AgentRunOutput);
    fixture.componentRef.setInput('entries', runEvents.flatMap(eventLogEntries));
    fixture.componentRef.setInput('connectivity', 'live');
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('uses a fixed virtual viewport without making high-volume output live', () => {
    const { element } = render();
    const viewport = element.querySelector('cdk-virtual-scroll-viewport');
    expect(viewport).not.toBeNull();
    expect(viewport?.getAttribute('role')).toBe('list');
    expect(viewport?.getAttribute('aria-live')).toBeNull();
    expect(element.textContent).toContain('9 lines');
  });

  it('filters visible output and emits pause, reconnect, and download intents', () => {
    const { fixture, element } = render();
    const pauses: boolean[] = [];
    let reconnects = 0;
    let downloads = 0;
    fixture.componentInstance.pauseChange.subscribe((value) => pauses.push(value));
    fixture.componentInstance.reconnectRequested.subscribe(() => (reconnects += 1));
    fixture.componentInstance.downloadRequested.subscribe(() => (downloads += 1));

    const search = element.querySelector('#run-output-search') as HTMLInputElement;
    search.value = '133 tests';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(element.textContent).toContain('1 lines');

    const pause = [...element.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')].find(
      (input) => input.closest('label')?.textContent?.includes('Pause'),
    )!;
    pause.checked = true;
    pause.dispatchEvent(new Event('change'));
    element.querySelectorAll<HTMLButtonElement>('.toolbar-action')[1]!.click();
    fixture.componentRef.setInput('connectivity', 'reconnecting');
    fixture.detectChanges();
    element.querySelector<HTMLButtonElement>('.output-status button')!.click();

    expect(pauses).toEqual([true]);
    expect(downloads).toBe(1);
    expect(reconnects).toBe(1);
  });
});

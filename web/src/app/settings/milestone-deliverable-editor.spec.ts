import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { MilestoneDeliverableEditor } from './milestone-deliverable-editor';
import { SettingsStore } from './settings.store';

Object.defineProperty(Range.prototype, 'getBoundingClientRect', {
  configurable: true,
  value: () => new DOMRect(),
});
Object.defineProperty(Range.prototype, 'getClientRects', {
  configurable: true,
  value: () => [],
});

const milestone = {
  key: 'public-beta',
  title: 'Public beta',
  priority: 'high',
  description: 'Deliver an installable beta.',
  requiredActivationTriggers: [],
};

function storeStub() {
  return {
    pending: signal(false),
    stale: signal(false),
    pendingExternal: signal(null),
    operationError: signal(null),
    settings: signal(null),
    isPending: () => false,
    errorForOperation: () => null,
    clearOperationError: vi.fn(),
    reviewLatest: vi.fn(),
    keepLatest: vi.fn(),
  } as unknown as SettingsStore;
}

describe('MilestoneDeliverableEditor', () => {
  it('keeps a dirty draft open until discard is explicitly confirmed', async () => {
    TestBed.configureTestingModule({
      imports: [MilestoneDeliverableEditor],
      providers: [{ provide: SettingsStore, useFactory: storeStub }],
    });
    const fixture = TestBed.createComponent(MilestoneDeliverableEditor);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('milestone', milestone);
    fixture.componentRef.setInput('activationTriggers', []);
    fixture.componentRef.setInput('priorityOptions', ['none', 'high']);
    const openChanges: boolean[] = [];
    fixture.componentInstance.openChange.subscribe((open) => openChanges.push(open));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const title = element.querySelector('#deliverable-title') as HTMLInputElement;
    title.value = 'Public beta candidate';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Close')!
      .click();
    fixture.detectChanges();

    expect(element.textContent).toContain('Discard unsaved milestone changes?');
    expect(openChanges).toEqual([]);
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Keep editing')!
      .click();
    fixture.detectChanges();
    expect((element.querySelector('#deliverable-title') as HTMLInputElement).value).toBe(
      'Public beta candidate',
    );

    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Close')!
      .click();
    fixture.detectChanges();
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Discard changes')!
      .click();
    expect(openChanges).toEqual([false]);
  });

  it('renders the complete deliverable read-only without mutation controls', async () => {
    TestBed.configureTestingModule({
      imports: [MilestoneDeliverableEditor],
      providers: [{ provide: SettingsStore, useFactory: storeStub }],
    });
    const fixture = TestBed.createComponent(MilestoneDeliverableEditor);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('milestone', milestone);
    fixture.componentRef.setInput('activationTriggers', [
      { key: 'beta-entry', title: 'Beta entry criteria', requirements: [] },
    ]);
    fixture.componentRef.setInput('priorityOptions', ['none', 'high']);
    fixture.componentRef.setInput('readOnly', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('This project is read-only.');
    expect((element.querySelector('#deliverable-title') as HTMLInputElement).disabled).toBe(true);
    expect((element.querySelector('.trigger-option input') as HTMLInputElement).disabled).toBe(
      true,
    );
    expect(element.textContent).not.toContain('Save title');
    expect(element.textContent).not.toContain('Review gate changes');
  });
});

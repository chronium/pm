import { TestBed } from '@angular/core/testing';

import { ActivationTriggerRenameDialog } from './activation-trigger-rename-dialog';

describe('ActivationTriggerRenameDialog', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [ActivationTriggerRenameDialog],
    }).compileComponents(),
  );

  it('keeps the immutable key and emits a trimmed changed title', async () => {
    const fixture = TestBed.createComponent(ActivationTriggerRenameDialog);
    fixture.componentRef.setInput('trigger', {
      key: 'beta-entry',
      title: 'Beta entry',
      isActive: false,
      activation: null,
      satisfiedRequirementCount: 0,
      requirementCount: 0,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: false,
      requirements: [],
      consumingMilestones: [],
    });
    fixture.componentRef.setInput('open', true);
    const renamed: string[] = [];
    fixture.componentInstance.renamed.subscribe((title) => renamed.push(title));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('beta-entry');
    const input = element.querySelector('input') as HTMLInputElement;
    input.value = '  Beta readiness  ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (element.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );
    expect(renamed).toEqual(['Beta readiness']);
  });

  it('requires confirmation before discarding a changed title', async () => {
    const fixture = TestBed.createComponent(ActivationTriggerRenameDialog);
    fixture.componentRef.setInput('trigger', {
      key: 'beta-entry',
      title: 'Beta entry',
      isActive: false,
      activation: null,
      satisfiedRequirementCount: 0,
      requirementCount: 0,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: false,
      requirements: [],
      consumingMilestones: [],
    });
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector('input') as HTMLInputElement;
    input.value = 'Changed';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Cancel')!
      .click();
    fixture.detectChanges();
    expect(element.textContent).toContain('Discard the unsaved title?');
  });
});

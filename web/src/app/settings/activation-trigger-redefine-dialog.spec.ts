import { TestBed } from '@angular/core/testing';

import { ActivationTriggerRedefineDialog } from './activation-trigger-redefine-dialog';

describe('ActivationTriggerRedefineDialog', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [ActivationTriggerRedefineDialog],
    }).compileComponents(),
  );

  it('rejects duplicate and blank requirements before review', async () => {
    const fixture = TestBed.createComponent(ActivationTriggerRedefineDialog);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('trigger', {
      key: 'beta-entry',
      title: 'Beta entry',
      isActive: true,
      activation: {
        at: '2026-08-07T06:00:00Z',
        mode: 'automatic',
        reason: null,
        waivedRequirements: [],
      },
      satisfiedRequirementCount: 1,
      requirementCount: 1,
      requirementsSatisfied: true,
      isLatchedDespiteUnmetRequirements: false,
      requirements: [
        { kind: 'task', source: 'PM-0001', isSatisfied: true, wasWaivedAtActivation: false },
      ],
      consumingMilestones: ['beta'],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    (element.querySelector('.add-requirement') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(element.textContent).toContain('Every requirement needs a source.');
    const source = element.querySelectorAll('input')[1] as HTMLInputElement;
    source.value = 'PM-0001';
    source.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(element.textContent).toContain('Duplicate requirements are not allowed.');
    expect(
      [...element.querySelectorAll<HTMLButtonElement>('button')].find((button) =>
        button.textContent?.includes('Review impact'),
      )?.disabled,
    ).toBe(true);
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { ActivationTriggerRequirementsDialog } from './activation-trigger-requirements-dialog';

describe('ActivationTriggerRequirementsDialog', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [ActivationTriggerRequirementsDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents(),
  );

  it('uses the shared selector to build a valid requirement definition', async () => {
    const fixture = TestBed.createComponent(ActivationTriggerRequirementsDialog);
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
      requirements: [],
      consumingMilestones: ['beta'],
    });
    fixture.componentRef.setInput('milestones', [
      { key: 'architecture-approved', title: 'Architecture approved' },
    ]);
    const reviews: Array<Array<{ kind: string; source: string }>> = [];
    fixture.componentInstance.review.subscribe((requirements) => reviews.push(requirements));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const kind = element.querySelector('select') as HTMLSelectElement;
    kind.value = 'milestone';
    kind.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const search = element.querySelector('input[type="search"]') as HTMLInputElement;
    search.focus();
    search.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Review impact'))!
      .click();
    expect(reviews).toEqual([[{ kind: 'milestone', source: 'architecture-approved' }]]);
  });

  it('saves inactive requirements directly without an impact preview', async () => {
    const fixture = TestBed.createComponent(ActivationTriggerRequirementsDialog);
    fixture.componentRef.setInput('open', true);
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
    fixture.componentRef.setInput('milestones', [
      { key: 'architecture-approved', title: 'Architecture approved' },
    ]);
    const saves: Array<Array<{ kind: string; source: string }>> = [];
    fixture.componentInstance.save.subscribe((requirements) => saves.push(requirements));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const kind = element.querySelector('select') as HTMLSelectElement;
    kind.value = 'milestone';
    kind.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const search = element.querySelector('input[type="search"]') as HTMLInputElement;
    search.focus();
    search.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Save requirements'))!
      .click();
    expect(saves).toEqual([[{ kind: 'milestone', source: 'architecture-approved' }]]);
    expect(element.textContent).not.toContain('Review impact');
  });
});

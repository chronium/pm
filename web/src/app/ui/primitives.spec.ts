import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PmBadge } from './badge/badge';
import { PmButton } from './button/button.directive';
import { PmConfirmDialog } from './confirm-dialog/confirm-dialog';
import { PmFormField } from './form-field/form-field';
import { PmEmptyState, PmErrorState, PmLoadingState } from './state/state';

@Component({
  imports: [PmBadge, PmButton, PmFormField, PmLoadingState, PmEmptyState, PmErrorState],
  template: `
    <button [pmButton]="variant">Action</button>
    <pm-badge [tone]="tone">Ready</pm-badge>
    <pm-form-field>
      <label for="title">Title</label>
      <input pmControl id="title" />
      <p pmFieldMessage>Required</p>
    </pm-form-field>
    <pm-loading-state>Loading tasks</pm-loading-state>
    <pm-empty-state>No tasks</pm-empty-state>
    <pm-error-state>Could not load</pm-error-state>
  `,
})
class PrimitiveHost {
  variant: 'primary' | 'secondary' | 'ghost' | 'danger' = 'primary';
  tone: 'neutral' | 'accent' | 'success' | 'warning' | 'danger' = 'success';
}

@Component({
  imports: [PmConfirmDialog],
  template: `
    <pm-confirm-dialog
      [(open)]="open"
      [pending]="pending"
      [heading]="heading"
      [message]="message"
      (confirmed)="confirmations = confirmations + 1"
      (cancelled)="cancellations = cancellations + 1"
    />
  `,
})
class DialogHost {
  open = true;
  pending = false;
  heading = 'Remove task?';
  message = '<img src=x onerror=alert(1)>';
  confirmations = 0;
  cancellations = 0;
}

describe('presentation primitives', () => {
  it('applies intentional button and badge variants', async () => {
    await TestBed.configureTestingModule({ imports: [PrimitiveHost] }).compileComponents();
    const fixture = TestBed.createComponent(PrimitiveHost);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button').classList).toContain('pm-button--primary');
    expect(fixture.nativeElement.querySelector('pm-badge span').classList).toContain('badge--success');
  });

  it('preserves projected native labeling and state semantics', async () => {
    await TestBed.configureTestingModule({ imports: [PrimitiveHost] }).compileComponents();
    const fixture = TestBed.createComponent(PrimitiveHost);
    fixture.detectChanges();
    const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(label.htmlFor).toBe(input.id);
    expect(label.control).toBe(input);
    expect(fixture.nativeElement.querySelector('pm-loading-state [role="status"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('pm-error-state [role="alert"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('pm-empty-state [role]')).toBeNull();
  });

  describe('destructive confirmation', () => {
    let fixture: ComponentFixture<DialogHost>;

    beforeEach(async () => {
      await TestBed.configureTestingModule({ imports: [DialogHost] }).compileComponents();
      fixture = TestBed.createComponent(DialogHost);
      fixture.detectChanges();
    });

    it('uses safe initial focus and emits confirmation without interpreting text as HTML', () => {
      const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(dialog.getAttribute('aria-labelledby')).toBe(dialog.querySelector('h2')?.id);
      expect(dialog.querySelector('button[autofocus]')).toBeTruthy();
      expect(dialog.querySelector('.dialog-body img')).toBeNull();
      expect(dialog.querySelector('.dialog-body p')?.textContent).toContain('<img');
      dialog.querySelectorAll('button')[1].click();
      expect(fixture.componentInstance.confirmations).toBe(1);
    });

    it('supports controlled cancellation and disables actions while pending', () => {
      const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
      buttons[0].click();
      fixture.detectChanges();
      expect(fixture.componentInstance.cancellations).toBe(1);
      expect(fixture.componentInstance.open).toBe(false);

      fixture.destroy();
      fixture = TestBed.createComponent(DialogHost);
      fixture.componentInstance.pending = true;
      fixture.detectChanges();
      const pendingButtons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
      expect(pendingButtons).toHaveLength(2);
      expect(pendingButtons[0].disabled).toBe(true);
      expect(pendingButtons[1].disabled).toBe(true);
      pendingButtons[1].click();
      expect(fixture.componentInstance.confirmations).toBe(0);
    });
  });
});

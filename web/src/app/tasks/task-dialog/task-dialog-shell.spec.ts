import { TestBed } from '@angular/core/testing';

import { TaskDialogShell } from './task-dialog-shell';

describe('TaskDialogShell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TaskDialogShell] }).compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  function render(backdropDismissible: boolean) {
    const fixture = TestBed.createComponent(TaskDialogShell);
    fixture.componentRef.setInput('dialogTitle', 'Task workspace');
    fixture.componentRef.setInput('backdropDismissible', backdropDismissible);
    fixture.detectChanges();
    return {
      fixture,
      dialog: fixture.nativeElement.querySelector('dialog') as HTMLDialogElement,
    };
  }

  it('emits a close intent when the backdrop is clicked and the workspace is clean', () => {
    const { fixture, dialog } = render(true);
    let closes = 0;
    fixture.componentInstance.closeIntent.subscribe(() => closes++);

    dialog.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(closes).toBe(1);
  });

  it('ignores backdrop clicks when the workspace has unsaved changes', () => {
    const { fixture, dialog } = render(false);
    let closes = 0;
    fixture.componentInstance.closeIntent.subscribe(() => closes++);

    dialog.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    dialog
      .querySelector('.task-dialog-frame')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(closes).toBe(0);
  });
});

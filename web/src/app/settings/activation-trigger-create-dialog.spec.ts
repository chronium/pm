import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { CreateActivationTriggerRequest } from './activation-api.service';
import { ActivationTriggerCreateDialog } from './activation-trigger-create-dialog';

describe('ActivationTriggerCreateDialog', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [ActivationTriggerCreateDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents(),
  );

  it('makes manual-only creation explicit and emits a normalized request', async () => {
    const fixture = TestBed.createComponent(ActivationTriggerCreateDialog);
    fixture.componentRef.setInput('open', true);
    const created: CreateActivationTriggerRequest[] = [];
    fixture.componentInstance.created.subscribe((request) => created.push(request));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Manual-only trigger');
    const submit = [...element.querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes('Create manual-only trigger'),
    )!;
    expect(submit.disabled).toBe(true);
    const inputs = element.querySelectorAll<HTMLInputElement>('.identity-fields input');
    inputs[0]!.value = '  launch-authorized  ';
    inputs[0]!.dispatchEvent(new Event('input'));
    inputs[1]!.value = '  Launch authorized  ';
    inputs[1]!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(submit.disabled).toBe(false);
    submit.click();
    expect(created).toEqual([
      { key: 'launch-authorized', title: 'Launch authorized', requirements: [] },
    ]);
  });

  it('protects a dirty draft before closing', async () => {
    const fixture = TestBed.createComponent(ActivationTriggerCreateDialog);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector('.identity-fields input') as HTMLInputElement;
    input.value = 'entry';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (element.querySelector('.dialog-close') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(element.textContent).toContain('Discard this unsaved activation trigger?');
  });
});

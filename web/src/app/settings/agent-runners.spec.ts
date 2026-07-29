import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { runnerRegistration, runnerStatus } from '../agent-runs/agent-runs.fixtures';
import { AgentRunners } from './agent-runners';

describe('AgentRunners', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AgentRunners],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render(registrations = [runnerRegistration]) {
    const fixture = TestBed.createComponent(AgentRunners);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/runners').flush(registrations);
    for (const runner of registrations) {
      http.expectOne('/api/v1/runners/' + runner.runnerId + '/status').flush(runnerStatus);
    }
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  it('shows capabilities, capacity, profiles, and public connection fingerprints', async () => {
    const { element } = await render();
    expect(element.textContent).toContain('Linux workstation');
    expect(element.textContent).toContain('1/3 active');
    expect(element.textContent).toContain('64 GiB memory');
    expect(element.textContent).toContain('pm-development');
    (element.querySelector('summary') as HTMLElement).click();
    expect(element.textContent).toContain(runnerRegistration.tlsFingerprint);
    expect(element.textContent).not.toContain('pairingCode');
  });

  it('keeps an offline registration visible with a local retry action', async () => {
    const fixture = TestBed.createComponent(AgentRunners);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/runners').flush([runnerRegistration]);
    http
      .expectOne('/api/v1/runners/runner-linux/status')
      .flush(
        { errorCode: 'runner_unavailable', detail: 'Runner is offline.' },
        { status: 503, statusText: 'Unavailable' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Linux workstation');
    expect(element.textContent).toContain('Offline');
    expect(element.textContent).toContain('Runner is offline.');
    element.querySelector('button') as HTMLButtonElement | null;
    const retry = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Retry status',
    )!;
    retry.click();
    http.expectOne('/api/v1/runners/runner-linux/status').flush(runnerStatus);
  });

  it('pairs from one-use identity details and never retains the submitted code', async () => {
    const { fixture, element, http } = await render([]);
    const pairButton = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Pair runner',
    )!;
    pairButton.click();
    fixture.detectChanges();
    const set = (selector: string, value: string) => {
      const input = element.querySelector(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    set('#runner-endpoint', runnerRegistration.endpoint);
    set('#runner-id', runnerRegistration.runnerId);
    set('#runner-fingerprint', runnerRegistration.tlsFingerprint);
    set('#runner-code', 'one-use-secret');
    fixture.detectChanges();
    (element.querySelector('.pairing-form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/v1/runners/pair');
    expect(request.request.body).toEqual({
      endpoint: runnerRegistration.endpoint,
      runnerId: runnerRegistration.runnerId,
      tlsFingerprint: runnerRegistration.tlsFingerprint,
      pairingCode: 'one-use-secret',
      replaceExisting: false,
    });
    request.flush(runnerRegistration, { status: 201, statusText: 'Created' });
    http.expectOne('/api/v1/runners/runner-linux/status').flush(runnerStatus);
    await fixture.whenStable();
    fixture.detectChanges();
    expect((element.querySelector('.pairing-dialog') as HTMLDialogElement).open).toBe(false);
    expect((element.querySelector('#runner-code') as HTMLInputElement).value).toBe('');
    expect(element.textContent).not.toContain('one-use-secret');
  });

  it('clears a rejected pairing code while preserving identity fields for correction', async () => {
    const { fixture, element, http } = await render([]);
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Pair runner')!
      .click();
    fixture.detectChanges();
    const values: Record<string, string> = {
      '#runner-endpoint': runnerRegistration.endpoint,
      '#runner-id': runnerRegistration.runnerId,
      '#runner-fingerprint': runnerRegistration.tlsFingerprint,
      '#runner-code': 'wrong-secret',
    };
    for (const [selector, value] of Object.entries(values)) {
      const input = element.querySelector(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    (element.querySelector('.pairing-form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http
      .expectOne('/api/v1/runners/pair')
      .flush(
        { errorCode: 'pairing_rejected', detail: 'Pairing code was rejected.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect((element.querySelector('#runner-code') as HTMLInputElement).value).toBe('');
    expect((element.querySelector('#runner-id') as HTMLInputElement).value).toBe(
      runnerRegistration.runnerId,
    );
    expect(element.textContent).toContain('Pairing code was rejected.');
  });
});

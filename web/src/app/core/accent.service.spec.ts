import { TestBed } from '@angular/core/testing';

import { AccentService } from './accent.service';

describe('AccentService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({});
    document.documentElement.removeAttribute('data-accent');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('defaults to teal and applies it to the document', () => {
    const service = TestBed.inject(AccentService);

    expect(service.preference()).toBe('teal');
    expect(service.label()).toBe('Teal');
    expect(document.documentElement.dataset['accent']).toBe('teal');
  });

  it('applies the project accent without writing browser storage', () => {
    const service = TestBed.inject(AccentService);
    const storage = vi.spyOn(Storage.prototype, 'setItem');

    service.applyProjectPreference('purple');

    expect(service.preference()).toBe('purple');
    expect(service.label()).toBe('Purple');
    expect(document.documentElement.dataset['accent']).toBe('purple');
    expect(storage).not.toHaveBeenCalled();
  });

  it('falls back to teal for an unknown project accent', () => {
    const service = TestBed.inject(AccentService);

    service.applyProjectPreference('infrared');

    expect(service.preference()).toBe('teal');
    expect(document.documentElement.dataset['accent']).toBe('teal');
  });
});

import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';

import { AccentService } from './accent.service';

describe('AccentService', () => {
  let values: Record<string, string>;
  let storage: Pick<Storage, 'getItem' | 'setItem'>;

  beforeEach(() => {
    values = {};
    storage = {
      getItem: vi.fn((key: string) => values[key] ?? null),
      setItem: vi.fn((key: string, value: string) => {
        values[key] = value;
      }),
    };
    TestBed.configureTestingModule({
      providers: [
        {
          provide: DOCUMENT,
          useValue: {
            defaultView: { sessionStorage: storage },
            documentElement: document.documentElement,
          },
        },
      ],
    });
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

  it('restores a valid per-tab accent preference', () => {
    storage.setItem('pm.accent', 'purple');

    const service = TestBed.inject(AccentService);

    expect(service.preference()).toBe('purple');
    expect(service.label()).toBe('Purple');
  });

  it('persists and applies a selected accent', () => {
    const service = TestBed.inject(AccentService);

    service.select('amber');

    expect(storage.setItem).toHaveBeenCalledWith('pm.accent', 'amber');
    expect(document.documentElement.dataset['accent']).toBe('amber');
  });

  it('ignores invalid storage and continues when storage is unavailable', () => {
    storage.setItem('pm.accent', 'infrared');
    const service = TestBed.inject(AccentService);
    expect(service.preference()).toBe('teal');

    vi.spyOn(storage, 'setItem').mockImplementation(() => {
      throw new Error('full');
    });
    expect(() => service.select('rose')).not.toThrow();
    expect(service.preference()).toBe('rose');
  });
});

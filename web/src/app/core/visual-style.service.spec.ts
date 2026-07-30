import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';

import { VisualStyleService } from './visual-style.service';

describe('VisualStyleService', () => {
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
    document.documentElement.removeAttribute('data-visual-style');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('defaults to the current style and applies it to the document', () => {
    const service = TestBed.inject(VisualStyleService);

    expect(service.style()).toBe('current');
    expect(document.documentElement.dataset['visualStyle']).toBe('current');
  });

  it('restores the exploration style from per-tab storage', () => {
    storage.setItem('pm.visual-style', 'exploration');

    const service = TestBed.inject(VisualStyleService);

    expect(service.style()).toBe('exploration');
    expect(document.documentElement.dataset['visualStyle']).toBe('exploration');
  });

  it('persists selection without sharing state through local storage', () => {
    const service = TestBed.inject(VisualStyleService);

    service.select('exploration');

    expect(service.style()).toBe('exploration');
    expect(storage.setItem).toHaveBeenCalledWith('pm.visual-style', 'exploration');
    expect(document.documentElement.dataset['visualStyle']).toBe('exploration');
  });

  it('continues in memory when session storage is unavailable', () => {
    vi.spyOn(storage, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const service = TestBed.inject(VisualStyleService);
    vi.spyOn(storage, 'setItem').mockImplementation(() => {
      throw new Error('full');
    });

    expect(() => service.select('exploration')).not.toThrow();
    expect(service.style()).toBe('exploration');
  });
});

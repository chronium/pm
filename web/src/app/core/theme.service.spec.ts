import { TestBed } from '@angular/core/testing';

import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let values: Record<string, string>;
  let storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem' | 'clear'>;

  beforeEach(() => {
    values = {};
    storage = {
      getItem: vi.fn((key: string) => values[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { values[key] = value; }),
      removeItem: vi.fn((key: string) => { delete values[key]; }),
      clear: vi.fn(() => { values = {}; }),
    };
    Object.defineProperty(window, 'localStorage', { configurable: true, value: storage });
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.removeAttribute('data-theme-preference');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    Reflect.deleteProperty(window, 'localStorage');
    TestBed.resetTestingModule();
  });

  it('defaults invalid storage safely to System', () => {
    storage.setItem('pm.theme', 'sepia');
    const service = TestBed.inject(ThemeService);
    expect(service.preference()).toBe('system');
    expect(service.iconName()).toBe('cssScreen');
    expect(service.actionLabel()).toBe('Theme: System. Switch to Light');
    expect(document.documentElement.dataset['themePreference']).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('initializes from a persisted preference and applies it to the document', () => {
    storage.setItem('pm.theme', 'dark');
    const service = TestBed.inject(ThemeService);
    expect(service.preference()).toBe('dark');
    expect(service.iconName()).toBe('cssMoon');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('cycles System to Light to Dark and persists every change', () => {
    const service = TestBed.inject(ThemeService);
    service.cycle();
    expect(service.preference()).toBe('light');
    expect(service.iconName()).toBe('cssSun');
    expect(service.actionLabel()).toBe('Theme: Light. Switch to Dark');
    expect(storage.getItem('pm.theme')).toBe('light');
    expect(document.documentElement.dataset['theme']).toBe('light');

    service.cycle();
    expect(service.preference()).toBe('dark');
    expect(service.iconName()).toBe('cssMoon');

    service.cycle();
    expect(service.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('continues to work when reading or writing storage fails', () => {
    const get = vi.spyOn(storage, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    const service = TestBed.inject(ThemeService);
    expect(service.preference()).toBe('system');
    get.mockRestore();
    vi.spyOn(storage, 'setItem').mockImplementation(() => { throw new Error('full'); });
    expect(() => service.cycle()).not.toThrow();
    expect(service.preference()).toBe('light');
    expect(document.documentElement.dataset['theme']).toBe('light');
  });
});

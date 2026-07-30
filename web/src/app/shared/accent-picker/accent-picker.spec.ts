import { TestBed } from '@angular/core/testing';

import { AccentPicker } from './accent-picker';

describe('AccentPicker', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [AccentPicker] }));

  afterEach(() => TestBed.resetTestingModule());

  it('renders labeled swatches and emits a project accent selection', () => {
    const fixture = TestBed.createComponent(AccentPicker);
    fixture.componentRef.setInput('preference', 'teal');
    const selection = vi.fn();
    fixture.componentInstance.selection.subscribe(selection);
    fixture.detectChanges();
    const options = fixture.nativeElement.querySelectorAll(
      'button',
    ) as NodeListOf<HTMLButtonElement>;

    expect([...options].map((option) => option.textContent?.trim())).toEqual([
      'Teal',
      'Blue',
      'Purple',
      'Rose',
      'Amber',
      'Neutral',
    ]);

    options[2]?.click();

    expect(selection).toHaveBeenCalledWith('purple');
    expect(options[0]?.getAttribute('aria-pressed')).toBe('true');
  });
});

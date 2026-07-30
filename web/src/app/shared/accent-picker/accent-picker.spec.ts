import { TestBed } from '@angular/core/testing';

import { AccentService } from '../../core/accent.service';
import { AccentPicker } from './accent-picker';

describe('AccentPicker', () => {
  beforeEach(() => {
    document.documentElement.removeAttribute('data-accent');
    TestBed.configureTestingModule({ imports: [AccentPicker] });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('renders labeled swatches and selects an accent', () => {
    const fixture = TestBed.createComponent(AccentPicker);
    fixture.detectChanges();
    const options = fixture.nativeElement.querySelectorAll(
      '.accent-menu button',
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
    fixture.detectChanges();

    expect(TestBed.inject(AccentService).preference()).toBe('purple');
    expect(document.documentElement.dataset['accent']).toBe('purple');
    expect(options[2]?.getAttribute('aria-pressed')).toBe('true');
  });
});

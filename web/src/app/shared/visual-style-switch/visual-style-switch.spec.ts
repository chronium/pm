import { TestBed } from '@angular/core/testing';

import { VisualStyleService } from '../../core/visual-style.service';
import { VisualStyleSwitch } from './visual-style-switch';

describe('VisualStyleSwitch', () => {
  beforeEach(() => {
    document.documentElement.removeAttribute('data-visual-style');
    TestBed.configureTestingModule({ imports: [VisualStyleSwitch] });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('selects and identifies each visual style', () => {
    const fixture = TestBed.createComponent(VisualStyleSwitch);
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll(
      'button',
    ) as NodeListOf<HTMLButtonElement>;

    expect(buttons[0]?.getAttribute('aria-pressed')).toBe('true');
    expect(buttons[1]?.getAttribute('aria-pressed')).toBe('false');

    buttons[1]?.click();
    fixture.detectChanges();

    expect(TestBed.inject(VisualStyleService).style()).toBe('exploration');
    expect(buttons[0]?.getAttribute('aria-pressed')).toBe('false');
    expect(buttons[1]?.getAttribute('aria-pressed')).toBe('true');
  });
});

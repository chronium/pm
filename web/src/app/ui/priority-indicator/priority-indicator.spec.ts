import { TestBed } from '@angular/core/testing';

import { PriorityIndicator } from './priority-indicator';

describe('PriorityIndicator', () => {
  function render(priority: string, source: string | null = null) {
    const fixture = TestBed.createComponent(PriorityIndicator);
    fixture.componentRef.setInput('priority', priority);
    fixture.componentRef.setInput('source', source);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the five priority levels as a clockwise fill progression', () => {
    const expected = [
      { priority: 'none', filled: false },
      { priority: 'low', filled: true },
      { priority: 'medium', filled: true },
      { priority: 'high', filled: true },
      { priority: 'urgent', filled: true },
    ];

    for (const state of expected) {
      const element = render(state.priority);
      expect(element.getAttribute('data-priority')).toBe(state.priority);
      expect(element.querySelector('.priority-fill') !== null).toBe(state.filled);
      expect(element.getAttribute('aria-label')).toBe(`Priority: ${state.priority}`);
    }
  });

  it('exposes priority provenance through its tooltip', () => {
    const element = render('high', 'milestone');

    expect(element.getAttribute('title')).toBe(
      'Priority: high — effective priority from milestone',
    );
  });

  it('falls back to an empty indicator for an unavailable priority value', () => {
    const element = render('unavailable');

    expect(element.getAttribute('data-priority')).toBe('none');
    expect(element.querySelector('.priority-fill')).toBeNull();
  });
});

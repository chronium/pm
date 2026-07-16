import { TestBed } from '@angular/core/testing';

import { MarkdownDisplay } from './markdown-display';
import { MarkdownEditor } from './markdown-editor';
import { MarkdownService } from './markdown.service';

describe('Markdown components', () => {
  it('renders Markdown and removes unsafe markup without bypassing Angular sanitization', () => {
    const fixture = TestBed.createComponent(MarkdownDisplay);
    fixture.componentRef.setInput(
      'markdown',
      '# Safe\n<script>alert(1)</script><img src=x onerror=alert(2)>',
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1')?.textContent).toBe('Safe');
    expect(fixture.nativeElement.querySelector('script')).toBeNull();
    expect(fixture.nativeElement.querySelector('img')?.hasAttribute('onerror')).toBe(false);
  });

  it('uses the shared renderer for sanitized editor previews', () => {
    const renderer = TestBed.inject(MarkdownService);
    const html = renderer.render('**Bold** <a href="javascript:alert(1)">bad</a>');
    expect(html).toContain('<strong>Bold</strong>');
    expect(html).not.toContain('javascript:');
  });

  it('initializes the editor, synchronizes external values, and restores the textarea on cleanup', async () => {
    const fixture = TestBed.createComponent(MarkdownEditor);
    fixture.componentRef.setInput('value', 'Initial');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentInstance.value.set('External update');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.componentInstance.ngOnDestroy();
    expect((fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement).value).toBe(
      'External update',
    );
    fixture.destroy();
  });
});

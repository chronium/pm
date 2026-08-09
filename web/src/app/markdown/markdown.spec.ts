import { TestBed } from '@angular/core/testing';

import { MarkdownDisplay } from './markdown-display';
import { MarkdownEditor } from './markdown-editor';
import { MarkdownService } from './markdown.service';
import { ProjectLinksService } from '../core/project-links.service';

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

  it('makes horizontally scrollable code blocks keyboard accessible', () => {
    const renderer = TestBed.inject(MarkdownService);

    const html = renderer.render('```ts\nconst value = "a long line";\n```');

    expect(html).toContain('<pre tabindex="0">');
  });

  it('rewrites available project links and degrades unavailable links to safe text', () => {
    TestBed.overrideProvider(ProjectLinksService, {
      useValue: {
        resolve: (href: string) =>
          href.includes('available')
            ? { kind: 'available', href: 'https://example.test/site/#/wiki/page', local: false }
            : href.startsWith('pm://')
              ? { kind: 'unavailable', reason: 'No published site.' }
              : { kind: 'not-project-link' },
      },
    });
    const renderer = TestBed.inject(MarkdownService);

    const html = renderer.render(
      '[Published](pm://project/available/wiki/page) [Missing](pm://project/missing/wiki/page)',
    );

    expect(html).toContain('href="https://example.test/site/#/wiki/page"');
    expect(html).toContain('class="pm-unavailable-link"');
    expect(html).toContain('title="No published site."');
    expect(html).toContain('aria-disabled="true"');
    expect(html).toContain('Unavailable: No published site.');
    expect(html).not.toContain('pm://');
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

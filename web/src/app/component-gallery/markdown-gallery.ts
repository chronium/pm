import { Component, signal } from '@angular/core';

import { MarkdownDisplay } from '../markdown/markdown-display';
import { MarkdownEditor } from '../markdown/markdown-editor';

@Component({
  selector: 'pm-markdown-gallery',
  imports: [MarkdownDisplay, MarkdownEditor],
  template: `
    <section class="component-page pm-frosted-surface pm-scroll-surface pm-component-surface">
      <header class="component-header">
        <p>Content</p>
        <h1>Markdown</h1>
      </header>

      <section class="specimen" aria-labelledby="markdown-editor-preview">
        <h2 id="markdown-editor-preview">Editor and preview</h2>
        <div class="markdown-pair">
          <section>
            <h3>Editor</h3>
            <pm-markdown-editor
              [(value)]="markdown"
              label="Markdown example"
              [externalPreview]="true"
            />
          </section>
          <section class="markdown-preview">
            <h3>Preview</h3>
            <pm-markdown-display [markdown]="markdown()" />
          </section>
        </div>
      </section>
    </section>
  `,
  styleUrls: ['./gallery-page.css', './markdown-gallery.css'],
})
export class MarkdownGallery {
  protected readonly markdown = signal(
    '# Component preview\n\nUse **Markdown** for task descriptions and wiki pages.\n\n- Dense\n- Readable\n- Sanitized',
  );
}

import { Component, computed, inject, input } from '@angular/core';

import { MarkdownService } from './markdown.service';

@Component({
  selector: 'pm-markdown-display',
  template: '<div class="markdown-body" [innerHTML]="html()"></div>',
  styleUrl: './markdown.css',
})
export class MarkdownDisplay {
  private readonly renderer = inject(MarkdownService);
  readonly markdown = input('');
  protected readonly html = computed(() => this.renderer.render(this.markdown()));
}

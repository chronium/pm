import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';
import DOMPurify from 'dompurify';
import { marked } from 'marked';

import { StaticProjectLinksService } from '../static/static-project-links.service';

@Injectable({ providedIn: 'root' })
export class MarkdownService {
  private readonly document = inject(DOCUMENT);
  private readonly projectLinks = inject(StaticProjectLinksService);

  render(markdown: string): string {
    const template = this.document.createElement('template');
    template.innerHTML = marked.parse(markdown, { async: false }) as string;
    for (const anchor of template.content.querySelectorAll<HTMLAnchorElement>('a[href]')) {
      const resolution = this.projectLinks.resolve(anchor.getAttribute('href') ?? '');
      if (resolution.kind === 'available') {
        anchor.href = resolution.href;
      } else if (resolution.kind === 'unavailable') {
        const replacement = this.document.createElement('span');
        replacement.className = 'pm-unavailable-link';
        replacement.title = resolution.reason;
        replacement.append(...Array.from(anchor.childNodes));
        anchor.replaceWith(replacement);
      }
    }
    return DOMPurify.sanitize(template.innerHTML);
  }
}

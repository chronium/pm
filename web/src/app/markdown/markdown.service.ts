import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';
import DOMPurify from 'dompurify';
import { marked } from 'marked';

import { ProjectLinksService } from '../core/project-links.service';

@Injectable({ providedIn: 'root' })
export class MarkdownService {
  private readonly document = inject(DOCUMENT);
  private readonly projectLinks = inject(ProjectLinksService);

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
        replacement.setAttribute('aria-disabled', 'true');
        replacement.append(...Array.from(anchor.childNodes));
        const explanation = this.document.createElement('span');
        explanation.className = 'pm-visually-hidden';
        explanation.textContent = ` Unavailable: ${resolution.reason}`;
        replacement.append(explanation);
        anchor.replaceWith(replacement);
      }
    }
    for (const codeBlock of template.content.querySelectorAll<HTMLElement>('pre')) {
      codeBlock.tabIndex = 0;
    }
    return DOMPurify.sanitize(template.innerHTML);
  }
}

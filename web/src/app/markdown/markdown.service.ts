import { Injectable } from '@angular/core';
import DOMPurify from 'dompurify';
import { marked } from 'marked';

@Injectable({ providedIn: 'root' })
export class MarkdownService {
  render(markdown: string): string {
    return DOMPurify.sanitize(marked.parse(markdown, { async: false }) as string);
  }
}

import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

export const STATIC_MODE_META = 'pm-site-mode';

export function isStaticDocument(document: Document = globalThis.document): boolean {
  return (
    document.querySelector<HTMLMetaElement>(`meta[name="${STATIC_MODE_META}"]`)?.content ===
    'static'
  );
}

@Injectable({ providedIn: 'root' })
export class StaticModeService {
  private readonly document = inject(DOCUMENT);
  readonly enabled = isStaticDocument(this.document);
  readonly snapshotUrl =
    this.document.querySelector<HTMLMetaElement>('meta[name="pm-site-snapshot"]')?.content ||
    './pm-snapshot.json';
  readonly generatedAt =
    this.document.querySelector<HTMLMetaElement>('meta[name="pm-site-generated-at"]')?.content ||
    null;
}

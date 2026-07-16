import { UrlMatcher, UrlSegment } from '@angular/router';

export const wikiPathMatcher: UrlMatcher = (segments) =>
  segments.length
    ? {
        consumed: segments,
        posParams: {
          wikiPath: new UrlSegment(segments.map((segment) => segment.path).join('/'), {}),
        },
      }
    : null;

export const wikiEditMatcher: UrlMatcher = (segments) => matchPrefixedPath(segments, 'edit');
export const wikiMetaMatcher: UrlMatcher = (segments) => matchPrefixedPath(segments, 'meta');

function matchPrefixedPath(segments: UrlSegment[], prefix: string) {
  return segments.length > 1 && segments[0]!.path === prefix
    ? {
        consumed: segments,
        posParams: {
          wikiPath: new UrlSegment(
            segments
              .slice(1)
              .map((segment) => segment.path)
              .join('/'),
            {},
          ),
        },
      }
    : null;
}

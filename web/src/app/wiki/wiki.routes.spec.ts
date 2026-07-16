import { UrlSegment } from '@angular/router';
import { wikiEditMatcher, wikiMetaMatcher, wikiPathMatcher } from './wiki.routes';

describe('wiki route matchers', () => {
  const segments = (...values: string[]) => values.map((value) => new UrlSegment(value, {}));

  it('joins decoded nested segments into one canonical path', () => {
    const match = wikiPathMatcher(segments('guides', 'C# & APIs'), null as never, null as never);
    expect(match?.consumed).toHaveLength(2);
    expect(match?.posParams?.['wikiPath']?.path).toBe('guides/C# & APIs');
  });

  it('requires a path after reserved edit and metadata prefixes', () => {
    expect(wikiEditMatcher(segments('edit'), null as never, null as never)).toBeNull();
    expect(wikiMetaMatcher(segments('meta'), null as never, null as never)).toBeNull();
    expect(
      wikiEditMatcher(segments('edit', 'nested', 'page'), null as never, null as never)
        ?.posParams?.['wikiPath']?.path,
    ).toBe('nested/page');
    expect(
      wikiMetaMatcher(segments('meta', 'nested', 'page'), null as never, null as never)
        ?.posParams?.['wikiPath']?.path,
    ).toBe('nested/page');
  });
});

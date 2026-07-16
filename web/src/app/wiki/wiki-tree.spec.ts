import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { WikiStore, type WikiTreeNode } from './wiki.store';
import { WikiTree } from './wiki-tree';

@Component({ template: '' })
class EmptyRoute {}

describe('WikiTree', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [WikiTree],
      providers: [
        provideRouter([{ path: 'wiki/:path', component: EmptyRoute }]),
        { provide: WikiStore, useValue: { expansionKey: () => 'test.wiki.expanded' } },
      ],
    }),
  );

  afterEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('moves the single active highlight when navigation changes', async () => {
    const nodes: WikiTreeNode[] = [leaf('first'), leaf('second')];
    const fixture = TestBed.createComponent(WikiTree);
    fixture.componentRef.setInput('nodes', nodes);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/wiki/first');
    fixture.detectChanges();
    expect(activeLinks(fixture.nativeElement)).toEqual(['/wiki/first']);

    await router.navigateByUrl('/wiki/second');
    fixture.detectChanges();
    expect(activeLinks(fixture.nativeElement)).toEqual(['/wiki/second']);
  });
});

function leaf(path: string): WikiTreeNode {
  return {
    name: path,
    path,
    page: { path, title: path, modifiedAt: '2026-01-01T00:00:00Z' },
    children: [],
  };
}

function activeLinks(root: HTMLElement): string[] {
  return [...root.querySelectorAll<HTMLAnchorElement>('a.active')].map((link) =>
    link.getAttribute('href')!,
  );
}

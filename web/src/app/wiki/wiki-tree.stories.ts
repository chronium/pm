import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';

import { WikiStore, type WikiTreeNode } from './wiki.store';
import { WikiTree } from './wiki-tree';

const leaf = (name: string, path: string, title = name): WikiTreeNode => ({ name, path, page: { path, title, modifiedAt: '2026-07-16T00:00:00Z' }, children: [] });
const nested: WikiTreeNode[] = [{ name: 'architecture', path: 'architecture', page: { path: 'architecture', title: 'Architecture', modifiedAt: '2026-07-16T00:00:00Z' }, children: [{ name: 'rendering', path: 'architecture/rendering', page: null, children: [leaf('canvas', 'architecture/rendering/canvas', 'Canvas pipeline'), leaf('textures', 'architecture/rendering/textures', 'Texture management with a deliberately long title that wraps safely')] }, leaf('routing', 'architecture/routing', 'Application routing')] }, leaf('overview', 'overview', 'Project overview')];
const many = Array.from({ length: 40 }, (_, index) => leaf(`page-${String(index + 1).padStart(2, '0')}`, `reference/page-${String(index + 1).padStart(2, '0')}`, `Reference page ${index + 1}`));

const meta = {
  title: 'Wiki/Navigation tree', component: WikiTree,
  decorators: [applicationConfig({ providers: [provideRouter([], withDisabledInitialNavigation()), { provide: WikiStore, useValue: { expansionKey: () => 'storybook.wiki.expanded' } }] })],
  parameters: { layout: 'padded' },
} satisfies Meta<WikiTree>;
export default meta;
type Story = StoryObj<typeof meta>;
export const NestedPageAndFolders: Story = { args: { nodes: nested } };
export const LargeTree: Story = { args: { nodes: [{ name: 'reference', path: 'reference', page: null, children: many }] } };
export const EmptyWiki: Story = { args: { nodes: [] } };
export const MobileWidth: Story = { args: { nodes: nested }, parameters: { viewport: { defaultViewport: 'mobile1' } } };

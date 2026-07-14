import type { StorybookConfig } from '@storybook/angular-vite';

const config: StorybookConfig = {
  stories: ['../src/**/*.stories.ts'],
  addons: ['@storybook/addon-a11y', '@storybook/addon-vitest'],
  framework: {
    name: '@storybook/angular-vite',
    options: {
      compodoc: false,
      tsconfig: './tsconfig.storybook.json',
    },
  },
};

export default config;

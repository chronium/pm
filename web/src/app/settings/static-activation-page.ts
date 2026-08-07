import { Component } from '@angular/core';

import { ActivationSwitchboard } from './activation-switchboard';

@Component({
  selector: 'pm-static-activation-page',
  imports: [ActivationSwitchboard],
  template: `
    <div class="static-activation-page pm-frosted-surface pm-scroll-surface pm-settings-surface">
      <header>
        <div>
          <h1>Project settings</h1>
          <p>Read-only published snapshot</p>
        </div>
      </header>
      <pm-activation-switchboard [readOnly]="true" />
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-height: 100%;
      background: transparent;
    }
    .static-activation-page {
      box-sizing: border-box;
      width: min(100%, 1024px);
      min-height: 100%;
      margin: 0 auto;
      padding: var(--pm-space-5) clamp(var(--pm-space-3), 4vw, var(--pm-space-6)) var(--pm-space-6);
      background: var(--pm-surface-raised);
    }
    header {
      margin-bottom: var(--pm-space-5);
      padding-bottom: var(--pm-space-3);
      border-bottom: 1px solid var(--pm-border-subtle);
    }
    h1 {
      margin: 0;
      font-size: var(--pm-font-size-lg);
    }
    p {
      margin: 2px 0 0;
      color: var(--pm-text-muted);
      font-size: var(--pm-font-size-sm);
    }
  `,
})
export class StaticActivationPage {}

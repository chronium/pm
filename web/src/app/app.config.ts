import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withHashLocation } from '@angular/router';

import { routes, staticRoutes } from './app.routes';
import { syncStatusInterceptor } from './core/sync-status.service';
import { isStaticDocument } from './static/static-mode.service';
import { staticSnapshotInterceptor } from './static/static-snapshot.interceptor';

const staticMode = isStaticDocument();

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([syncStatusInterceptor, staticSnapshotInterceptor])),
    provideRouter(
      staticMode ? staticRoutes : routes,
      withComponentInputBinding(),
      ...(staticMode ? [withHashLocation()] : []),
    ),
  ],
};

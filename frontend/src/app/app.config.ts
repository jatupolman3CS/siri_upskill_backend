import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { registerLocaleData } from '@angular/common';
import localeTh from '@angular/common/locales/th';
import { provideClientHydration } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/http/auth.interceptor';
import { errorInterceptor } from './core/http/error.interceptor';

// Required for any `DatePipe` use with the `'th-TH'` locale (P0-35's DevicesPage formats
// UTC session timestamps as `th-TH` / `Asia/Bangkok`, per frontend.md's date-formatting rule) —
// Angular throws NG0701 ("Missing locale data") for a locale it hasn't been given data for.
// Pure data registration, no `window`/`document` access, so it is safe to run at module load on
// both the server and browser entry points (both import this file).
registerLocaleData(localeTh);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideClientHydration(),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, errorInterceptor])),
  ],
};

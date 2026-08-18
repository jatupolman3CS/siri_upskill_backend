import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./layouts/public-layout/public-layout').then((m) => m.PublicLayout),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/home/home-page/home-page').then((m) => m.HomePage),
      },
      // Auth pages (P0-35) — unauthenticated flows against the real Identity API. See
      // app.routes.server.ts for each route's SSR/CSR rendering decision and why.
      {
        path: 'register',
        loadComponent: () =>
          import('./features/auth/register/register-page').then((m) => m.RegisterPage),
      },
      {
        path: 'login',
        loadComponent: () =>
          import('./features/auth/login/login-page').then((m) => m.LoginPage),
      },
      {
        path: 'confirm-email',
        loadComponent: () =>
          import('./features/auth/confirm-email/confirm-email-page').then((m) => m.ConfirmEmailPage),
      },
      {
        path: 'forgot-password',
        loadComponent: () =>
          import('./features/auth/forgot-password/forgot-password-page').then((m) => m.ForgotPasswordPage),
      },
      {
        path: 'reset-password',
        loadComponent: () =>
          import('./features/auth/reset-password/reset-password-page').then((m) => m.ResetPasswordPage),
      },
      // Authenticated (authGuard) — the only authenticated route this task builds; see
      // core/guards/auth.guard.ts and app.routes.server.ts for why this is CSR-only.
      {
        path: 'account/devices',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/auth/devices/devices-page').then((m) => m.DevicesPage),
      },
    ],
  },
  // TODO: /learn (LearnLayout), /instructor (InstructorLayout), /admin
  // (AdminLayout) get their own top-level route subtrees + guards once
  // those features exist (see ARCHITECTURE.md section 3).
];

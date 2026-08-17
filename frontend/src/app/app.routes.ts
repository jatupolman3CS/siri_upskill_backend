import { Routes } from '@angular/router';

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
    ],
  },
  // TODO: /learn (LearnLayout), /instructor (InstructorLayout), /admin
  // (AdminLayout) get their own top-level route subtrees + guards once
  // those features exist (see ARCHITECTURE.md section 3).
];

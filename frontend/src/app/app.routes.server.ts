import { RenderMode, ServerRoute } from '@angular/ssr';

// TODO: once /learn, /instructor, /admin routes exist, split them out here
// with RenderMode.Client (+ noindex) per frontend.md — only public pages
// should be RenderMode.Server. Everything is Server-rendered for now since
// the only route is the public home page.
export const serverRoutes: ServerRoute[] = [
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];

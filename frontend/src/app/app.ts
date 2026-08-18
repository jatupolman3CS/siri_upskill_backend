import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { ToastHost } from './shared/toast/toast-host';

@Component({
  selector: 'app-root',
  // ToastHost mounted once here (P0-34) so `ToastService.success()/error()/...`
  // has somewhere to render, no matter which route is active.
  imports: [RouterOutlet, ToastHost],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}

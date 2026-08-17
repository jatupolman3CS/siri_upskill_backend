import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * PLACEHOLDER shell for the `/admin` CSR area (CMS, marketing, revenue,
 * users nav — see ARCHITECTURE.md section 3). Not wired to any auth/role
 * guard yet; real chrome + guard land with the Admin feature.
 */
@Component({
  selector: 'app-admin-layout',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminLayout {}

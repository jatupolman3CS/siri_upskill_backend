import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * PLACEHOLDER shell for the `/learn` CSR area (video player, progress
 * sidebar, resume UI — see ARCHITECTURE.md section 4 and ui-design.md).
 * Not wired to any auth guard yet; real chrome + route guard land with
 * the Learning feature.
 */
@Component({
  selector: 'app-learn-layout',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LearnLayout {}

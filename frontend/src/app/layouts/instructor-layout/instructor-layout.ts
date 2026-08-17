import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * PLACEHOLDER shell for the `/instructor` CSR area (course builder,
 * analytics, announcements nav — see ARCHITECTURE.md section 3). Not
 * wired to any auth/role guard yet; real chrome + guard land with the
 * Instructor feature.
 */
@Component({
  selector: 'app-instructor-layout',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InstructorLayout {}

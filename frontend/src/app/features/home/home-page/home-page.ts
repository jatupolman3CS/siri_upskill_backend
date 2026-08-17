import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { TranslationService } from '../../../core/i18n/translation.service';

/**
 * Minimal home route — just enough real content to prove the app builds,
 * serves, and server-renders end-to-end. The real marketing/catalog home
 * page is a separate feature task once the design system exists.
 */
@Component({
  selector: 'app-home-page',
  imports: [],
  templateUrl: './home-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  protected readonly translation = inject(TranslationService);
}

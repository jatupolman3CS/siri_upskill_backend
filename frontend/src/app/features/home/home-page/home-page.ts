import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { setPageMeta } from '../../../core/seo/page-meta';

/**
 * Home route — hero section only (heading/subheading + CTA to register/login). Featured
 * courses/social proof sections are task P1-23 and need real Catalog API data — see
 * frontend/design-system/siri-upskill/MASTER.md's own note that the full
 * Hero+Feature+SocialProof+CTA pattern is "a starting point for the home/landing page (P1)".
 * Deliberately not faked here with placeholder course/testimonial content.
 */
@Component({
  selector: 'app-home-page',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './home-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  private readonly translation = inject(TranslationService);

  constructor() {
    setPageMeta({
      title: this.translation.t('common.appName'),
      description: this.translation.t('home.pageDescription'),
    });
  }
}

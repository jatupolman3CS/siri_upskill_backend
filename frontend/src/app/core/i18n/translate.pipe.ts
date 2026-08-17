import { Pipe, PipeTransform, inject } from '@angular/core';

import { TranslationService } from './translation.service';

/**
 * Template ergonomics wrapper around `TranslationService.t()`:
 * `{{ 'common.loading' | translate }}`.
 *
 * Stays reactive to locale changes despite being a pure pipe: it reads
 * `TranslationService.dictionary()` (a signal) during `transform`, so the
 * signal becomes a tracked dependency of the enclosing view the same way a
 * signal read directly in a template would.
 */
@Pipe({ name: 'translate', pure: true })
export class TranslatePipe implements PipeTransform {
  private readonly translation = inject(TranslationService);

  transform(key: string): string {
    return this.translation.t(key);
  }
}

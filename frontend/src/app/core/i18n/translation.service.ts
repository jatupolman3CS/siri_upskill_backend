import { Injectable, computed, signal } from '@angular/core';

import en from './en.json';
import th from './th.json';

export type Locale = 'th' | 'en';

type TranslationDictionary = typeof th;

const DICTIONARIES: Record<Locale, TranslationDictionary> = { th, en };
const DEFAULT_LOCALE: Locale = 'th';

/**
 * Lightweight runtime i18n for TH/EN.
 *
 * INTENTIONAL DESIGN CHOICE — not an oversight: this project does NOT use
 * Angular's built-in `@angular/localize` compile-time i18n. `@angular/localize`
 * bakes exactly one locale into a given build (you ship a separate bundle
 * per locale, chosen at build/deploy time). SIRI UpSkill needs learners and
 * instructors to switch between Thai and English at runtime, in a single
 * build/deploy, without a page reload hitting a different bundle — so a
 * small signal-based dictionary service is the deliberate choice here.
 *
 * Usage: inject `TranslationService` and call `t('common.loading')`, or use
 * the `translate` pipe in templates: `{{ 'common.loading' | translate }}`.
 * Per frontend.md, no user-facing Thai/English string may be hardcoded
 * directly in a component template or .ts file — it must go through a key
 * in th.json / en.json instead.
 */
@Injectable({ providedIn: 'root' })
export class TranslationService {
  private readonly _locale = signal<Locale>(DEFAULT_LOCALE);

  /** Current active locale. */
  readonly locale = this._locale.asReadonly();

  /** Active dictionary, recomputed whenever locale changes. */
  readonly dictionary = computed(() => DICTIONARIES[this._locale()]);

  // TODO: once there's a settled UX for it, initialize from a saved user
  // preference / Accept-Language and persist changes (cookie, not
  // localStorage, so SSR can read it too). Deliberately not done yet to
  // keep this scaffold SSR-safe with zero browser API access.
  setLocale(locale: Locale): void {
    this._locale.set(locale);
  }

  /**
   * Resolves a dot-path key against the active dictionary, e.g.
   * `t('common.loading')`. Returns the key itself when not found, so a
   * missing translation is visible in the UI instead of silently blank.
   */
  t(key: string): string {
    const value = resolveKey(this.dictionary(), key);
    return typeof value === 'string' ? value : key;
  }
}

function resolveKey(source: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>((current, segment) => {
    if (current !== null && typeof current === 'object' && segment in current) {
      return (current as Record<string, unknown>)[segment];
    }
    return undefined;
  }, source);
}

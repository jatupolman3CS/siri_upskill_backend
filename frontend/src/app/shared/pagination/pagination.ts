import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { phosphorCaretLeft, phosphorCaretRight } from '@ng-icons/phosphor-icons/regular';

import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { buildPaginationItems } from './pagination.logic';

/**
 * Page-number navigation for the shared UI kit (P0-34). Truncation logic
 * (first/last + sliding window + ellipsis) lives in `pagination.logic.ts`
 * as a plain function — see that file's doc comment and
 * `pagination.logic.spec.ts` for why and how it's tested.
 */
@Component({
  selector: 'app-pagination',
  imports: [NgIcon, TranslatePipe],
  templateUrl: './pagination.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideIcons({ phosphorCaretLeft, phosphorCaretRight })],
  host: {
    class: 'block',
  },
})
export class Pagination {
  readonly currentPage = model.required<number>();
  readonly totalPages = input.required<number>();
  readonly siblingCount = input(1);

  protected readonly items = computed(() =>
    buildPaginationItems(this.currentPage(), this.totalPages(), this.siblingCount()),
  );

  protected readonly isFirstPage = computed(() => this.currentPage() <= 1);
  protected readonly isLastPage = computed(() => this.currentPage() >= this.totalPages());

  protected goToPage(page: number): void {
    if (page === this.currentPage() || page < 1 || page > this.totalPages()) {
      return;
    }
    this.currentPage.set(page);
  }

  protected goToPrevious(): void {
    this.goToPage(this.currentPage() - 1);
  }

  protected goToNext(): void {
    this.goToPage(this.currentPage() + 1);
  }
}

/** A page number, or a collapsed run of hidden pages between two shown numbers. */
export type PaginationItem = number | 'ellipsis';

/**
 * Builds the first/last + sliding-window + ellipsis pattern used by
 * `Pagination`. Kept as a pure function (no Angular, no DOM) specifically
 * so the truncation algorithm — the trickiest logic in this component —
 * can be unit-tested directly and exhaustively, independent of rendering.
 *
 * - Small page counts: every page is shown, no ellipsis (nothing to
 *   truncate).
 * - Large page counts: always show page 1 and the last page, plus a
 *   window of `siblingCount` pages on each side of `currentPage`, with a
 *   single `'ellipsis'` marker standing in for any gap wider than one
 *   page. A one-page gap is filled in with the real number instead —
 *   collapsing a single page behind an ellipsis marker would be
 *   pointless, it's not saving any space.
 */
export function buildPaginationItems(
  currentPage: number,
  totalPages: number,
  siblingCount = 1,
): PaginationItem[] {
  if (totalPages <= 0) {
    return [];
  }

  const safeCurrent = Math.min(Math.max(currentPage, 1), totalPages);
  const safeSiblingCount = Math.max(0, siblingCount);

  // Total slots if every page in [1..totalPages] were shown as a plain
  // number: first, last, current, siblings on both sides, and two
  // ellipsis-avoidance buffer pages. Below this, showing everything is
  // both simpler and no wider than the truncated version would be.
  const totalVisibleWhenExpanded = safeSiblingCount * 2 + 5;
  if (totalPages <= totalVisibleWhenExpanded) {
    return range(1, totalPages);
  }

  const leftSiblingIndex = Math.max(safeCurrent - safeSiblingCount, 1);
  const rightSiblingIndex = Math.min(safeCurrent + safeSiblingCount, totalPages);

  // "> 3" / "< totalPages - 2", not "> 2" / "< totalPages - 1": the latter,
  // more obvious-looking pair is off by one — it collapses an exactly
  // one-page gap (e.g. leftSiblingIndex === 3, hiding only page 2) behind
  // an ellipsis, which is exactly the pointless case this function's doc
  // comment says not to do. The tighter threshold only shows an ellipsis
  // once it is actually hiding 2+ pages.
  const showLeftEllipsis = leftSiblingIndex > 3;
  const showRightEllipsis = rightSiblingIndex < totalPages - 2;

  if (!showLeftEllipsis && showRightEllipsis) {
    const leftItemCount = 3 + safeSiblingCount * 2;
    return [...range(1, leftItemCount), 'ellipsis', totalPages];
  }

  if (showLeftEllipsis && !showRightEllipsis) {
    const rightItemCount = 3 + safeSiblingCount * 2;
    return [1, 'ellipsis', ...range(totalPages - rightItemCount + 1, totalPages)];
  }

  return [1, 'ellipsis', ...range(leftSiblingIndex, rightSiblingIndex), 'ellipsis', totalPages];
}

function range(start: number, end: number): number[] {
  const length = Math.max(0, end - start + 1);
  return Array.from({ length }, (_, i) => start + i);
}

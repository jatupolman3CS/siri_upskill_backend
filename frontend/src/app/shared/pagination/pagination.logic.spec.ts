import { buildPaginationItems } from './pagination.logic';

describe('buildPaginationItems', () => {
  it('returns an empty list for zero (or fewer) pages', () => {
    expect(buildPaginationItems(1, 0)).toEqual([]);
    expect(buildPaginationItems(1, -3)).toEqual([]);
  });

  it('returns a single page for a 1-page list', () => {
    expect(buildPaginationItems(1, 1)).toEqual([1]);
  });

  it('shows every page, with no ellipsis, when the count is small', () => {
    expect(buildPaginationItems(1, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(buildPaginationItems(3, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(buildPaginationItems(5, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(buildPaginationItems(4, 7)).toEqual([1, 2, 3, 4, 5, 6, 7]);
  });

  it('shows a right ellipsis only when current page is near the start', () => {
    expect(buildPaginationItems(1, 20)).toEqual([1, 2, 3, 4, 5, 'ellipsis', 20]);
    expect(buildPaginationItems(2, 20)).toEqual([1, 2, 3, 4, 5, 'ellipsis', 20]);
  });

  it('shows a left ellipsis only when current page is near the end', () => {
    expect(buildPaginationItems(19, 20)).toEqual([1, 'ellipsis', 16, 17, 18, 19, 20]);
    expect(buildPaginationItems(20, 20)).toEqual([1, 'ellipsis', 16, 17, 18, 19, 20]);
  });

  it('shows both ellipses with a sibling window when current page is in the middle', () => {
    expect(buildPaginationItems(10, 20)).toEqual([1, 'ellipsis', 9, 10, 11, 'ellipsis', 20]);
  });

  it('always includes the current page inside the window', () => {
    for (let current = 1; current <= 20; current++) {
      const items = buildPaginationItems(current, 20);
      expect(items).toContain(current);
    }
  });

  it('never collapses a real one-page gap behind an ellipsis (not worth the space it "saves")', () => {
    // total=9, current=4, sibling=1 -> sibling window is [3,4,5], so the
    // left side would only be hiding page 2 alone. That must be shown
    // directly ("1 2 3 4 5 … 9"), not collapsed to "1 … 3 4 5 … 9".
    const items = buildPaginationItems(4, 9, 1);
    expect(items).toEqual([1, 2, 3, 4, 5, 'ellipsis', 9]);
  });

  it('clamps an out-of-range current page instead of producing a broken window', () => {
    expect(buildPaginationItems(999, 10)).toEqual(buildPaginationItems(10, 10));
    expect(buildPaginationItems(0, 10)).toEqual(buildPaginationItems(1, 10));
    expect(buildPaginationItems(-5, 10)).toEqual(buildPaginationItems(1, 10));
  });

  it('respects a larger siblingCount', () => {
    expect(buildPaginationItems(10, 20, 2)).toEqual([1, 'ellipsis', 8, 9, 10, 11, 12, 'ellipsis', 20]);
  });

  it('every item is either a positive integer <= totalPages, or the ellipsis marker', () => {
    const items = buildPaginationItems(13, 37, 1);
    for (const item of items) {
      if (item !== 'ellipsis') {
        expect(item).toBeGreaterThanOrEqual(1);
        expect(item).toBeLessThanOrEqual(37);
      }
    }
  });

  it('never produces a duplicate page number', () => {
    const items = buildPaginationItems(13, 37, 1).filter((item): item is number => item !== 'ellipsis');
    expect(new Set(items).size).toBe(items.length);
  });
});

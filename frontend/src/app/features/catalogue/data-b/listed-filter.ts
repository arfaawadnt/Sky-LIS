import type { MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import type { CatalogueBQuery } from './catalogue-b.api';

/**
 * The filter a master-data register last listed with (search text and status): the page's CSV export sends it, so the
 * file holds exactly the rows of the grid (UAT C-04 / B-09).
 */
export class ListedFilter {
  private current: { readonly search: string; readonly isActive: boolean | null } = { search: '', isActive: true };

  /** Called by the register's `list` adapter; answers the search and status part of the list query. */
  track(filter: MasterDataFilter): { readonly search: string; readonly isActive: boolean | null } {
    this.current = { search: filter.search, isActive: filter.active };
    return this.current;
  }

  /** The export query of the rows on screen. */
  query(): CatalogueBQuery {
    return this.current;
  }
}

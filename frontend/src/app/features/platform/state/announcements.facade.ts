import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { AnnouncementFilter, AnnouncementPlacement, AnnouncementRequest, AnnouncementView } from '../../../core/api/announcement.contracts';
import type { PageRequest } from '../../../core/api/paging';
import type { ClientListItem, CountryListItem, PlanListItem } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { AnnouncementsApi } from '../data/announcements.api';
import { PlatformApi } from '../data/platform.api';

/** The server keys of the sortable columns (AnnouncementSortKeys): the title by the interface language. */
export const ANNOUNCEMENT_SORT_KEYS: SortKeyMap = {
  title: (lang: string) => (lang === 'ar' ? 'titleAr' : 'title'),
  placement: 'placement',
  fromUtc: 'fromUtc',
  toUtc: 'toUtc',
  impressions: 'impressions',
  clicks: 'clicks',
};

/** The most rows the audience pick lists read (the server's page ceiling). */
const PICK_LIST_SIZE = 200;

/**
 * SA-22 state (LIS-CR-013, C-17): the register (search, placement, active filter, server sort and paging), the pick lists
 * of the audience (countries, plans, clients), and the writes — save, activate / deactivate, image upload / removal.
 */
@Injectable()
export class AnnouncementsFacade {
  private readonly api = inject(AnnouncementsApi);
  private readonly platform = inject(PlatformApi);
  private readonly i18n = inject(I18nService);

  readonly filter = signal<AnnouncementFilter>({ search: null, placement: null, isActive: null, page: 1, pageSize: 50 });

  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<AnnouncementFilter>(() => ({ ...this.filter(), ...sortParams(serverSortOf(this.sort(), ANNOUNCEMENT_SORT_KEYS, () => this.i18n.lang())) }));

  readonly announcements = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.list(params, abortSignal),
  });

  readonly rows = computed<readonly AnnouncementView[]>(() => (this.announcements.hasValue() ? this.announcements.value().items : []));

  readonly countries = resource({
    loader: ({ abortSignal }) => this.platform.listCountries({ search: null, isActive: null, page: 1, pageSize: PICK_LIST_SIZE }, abortSignal),
  });

  readonly plans = resource({
    loader: ({ abortSignal }) => this.platform.listPlans(null, { page: 1, pageSize: PICK_LIST_SIZE }, abortSignal),
  });

  readonly clients = resource({
    loader: ({ abortSignal }) => this.platform.listClients({ search: null, source: null, planId: null, status: null, page: 1, pageSize: PICK_LIST_SIZE }, abortSignal),
  });

  readonly countryOptions = computed<readonly CountryListItem[]>(() => (this.countries.hasValue() ? this.countries.value().items : []));
  readonly planOptions = computed<readonly PlanListItem[]>(() => (this.plans.hasValue() ? this.plans.value().items : []));
  readonly clientOptions = computed<readonly ClientListItem[]>(() => (this.clients.hasValue() ? this.clients.value().items : []));

  /** Create (id null) or edit; answers the id of the saved announcement so the editor can go on with its image. */
  readonly save = mutation(
    async (input: { id: string | null; request: AnnouncementRequest }): Promise<string> => {
      if (input.id === null) {
        return (await this.api.create(input.request)).id;
      }

      await this.api.update(input.id, input.request);
      return input.id;
    },
    { then: () => this.announcements.reload() },
  );

  readonly setActive = mutation((input: { id: string; active: boolean }) => this.api.setActive(input.id, input.active), { then: () => this.announcements.reload() });

  readonly uploadImage = mutation((input: { id: string; file: File }) => this.api.uploadImage(input.id, input.file), { then: () => this.announcements.reload() });

  readonly removeImage = mutation((id: string) => this.api.removeImage(id), { then: () => this.announcements.reload() });

  /** The editor's preview of an image (the admin endpoint needs the bearer token, so it is read as a blob). */
  image(id: string): Promise<Blob> {
    return this.api.image(id);
  }

  get(id: string): Promise<AnnouncementView> {
    return this.api.get(id);
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search: search === '' ? null : search, page: 1 }));
  }

  setPlacement(placement: AnnouncementPlacement | null): void {
    this.filter.update((current) => ({ ...current, placement, page: 1 }));
  }

  setActiveFilter(isActive: boolean | null): void {
    this.filter.update((current) => ({ ...current, isActive, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  /** Re-reads everything when the page opens again: the facade outlives the page (route provider; UAT D-14). */
  refresh(): void {
    this.announcements.reload();
    this.countries.reload();
    this.plans.reload();
    this.clients.reload();
  }
}

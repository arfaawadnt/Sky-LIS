import { Injectable, type WritableSignal, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import {
  type NotificationTemplateListItem,
  type NotificationTemplateRequest,
  PLATFORM_API,
  type PcrKitListItem,
  type PcrKitRequest,
  type ReferenceListFilter,
} from '../../../core/api/platform.contracts';
import type { SettingValueItem, TenantSettingItem } from '../../../core/api/settings.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { PlatformApi } from '../data/platform.api';

/** The server keys of the sortable columns of the PCR kit library (B-03, PlatformAdminSortKeys.PcrKits). */
export const PCR_KIT_SORT_KEYS: SortKeyMap = { name: 'name', analyserModelName: 'analyserModel' };

/** The server key of the sortable column of the notification templates (B-03, PlatformAdminSortKeys.NotificationTemplates). */
export const TEMPLATE_SORT_KEYS: SortKeyMap = { code: 'code' };

/** The list state every hand-written reference register shares: search, active filter, page. */
function listFilter(): WritableSignal<ReferenceListFilter> {
  return signal<ReferenceListFilter>({ search: null, isActive: true, page: 1, pageSize: 50 });
}

/** SA-16 PCR kit templates: list, save (create / edit), activate, and the analyser models a kit may reference. */
@Injectable()
export class PcrKitsFacade {
  private readonly api = inject(PlatformApi);
  private readonly i18n = inject(I18nService);

  readonly filter = listFilter();

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<ReferenceListFilter>(() => ({ ...this.filter(), ...sortParams(serverSortOf(this.sort(), PCR_KIT_SORT_KEYS, () => this.i18n.lang())) }));

  readonly kits = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.listPcrKits(params, abortSignal),
  });

  readonly models = resource({
    loader: ({ abortSignal }) => this.api.listAnalyserModels({ search: null, isActive: true, page: 1, pageSize: 200 }, abortSignal),
  });

  readonly rows = computed<readonly PcrKitListItem[]>(() => (this.kits.hasValue() ? this.kits.value().items : []));

  readonly save = mutation((input: { id: string | null; request: PcrKitRequest }) => this.api.savePcrKit(input.id, input.request), { then: () => this.kits.reload() });

  readonly setActive = mutation((input: { id: string; active: boolean }) => this.api.setLibraryActive(PLATFORM_API.pcrKits, input.id, input.active), { then: () => this.kits.reload() });

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search: search === '' ? null : search, page: 1 }));
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

  /** Re-reads the kits and the analyser models when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.kits.reload();
    this.models.reload();
  }
}

/** SA-20 notification templates: list, save, activate, and the placeholder help served by the API. */
@Injectable()
export class TemplatesFacade {
  private readonly api = inject(PlatformApi);
  private readonly i18n = inject(I18nService);

  readonly filter = listFilter();

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<ReferenceListFilter>(() => ({ ...this.filter(), ...sortParams(serverSortOf(this.sort(), TEMPLATE_SORT_KEYS, () => this.i18n.lang())) }));

  readonly templates = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.listTemplates(params, abortSignal),
  });

  readonly placeholders = resource({
    loader: () => this.api.templatePlaceholders(),
  });

  readonly rows = computed<readonly NotificationTemplateListItem[]>(() => (this.templates.hasValue() ? this.templates.value().items : []));

  readonly save = mutation(
    (input: { id: string | null; request: NotificationTemplateRequest }): Promise<unknown> =>
      input.id === null ? this.api.createTemplate(input.request) : this.api.updateTemplate(input.id, input.request),
    { then: () => this.templates.reload() },
  );

  readonly setActive = mutation((input: { id: string; active: boolean }) => this.api.setTemplateActive(input.id, input.active), { then: () => this.templates.reload() });

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search: search === '' ? null : search, page: 1 }));
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

  /** Re-reads the templates and the placeholders when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.templates.reload();
    this.placeholders.reload();
  }
}

/** SA-19 platform parameters: the settings of the platform tenant, which are the defaults of every laboratory. */
@Injectable()
export class ParametersFacade {
  private readonly api = inject(PlatformApi);

  readonly settings = resource({
    loader: ({ abortSignal }) => this.api.settings(abortSignal),
  });

  readonly items = computed<readonly TenantSettingItem[]>(() => (this.settings.hasValue() ? this.settings.value().settings : []));

  readonly save = mutation((changes: readonly SettingValueItem[]) => this.api.saveSettings({ settings: changes }), { then: () => this.settings.reload() });
  /** Re-reads the parameters when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.settings.reload();
  }
}

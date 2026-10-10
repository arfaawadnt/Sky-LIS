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
import { mutation } from '../../../core/state/mutation';
import { PlatformApi } from '../data/platform.api';

/** The list state every hand-written reference register shares: search, active filter, page. */
function listFilter(): WritableSignal<ReferenceListFilter> {
  return signal<ReferenceListFilter>({ search: null, isActive: true, page: 1, pageSize: 50 });
}

/** SA-16 PCR kit templates: list, save (create / edit), activate, and the analyser models a kit may reference. */
@Injectable()
export class PcrKitsFacade {
  private readonly api = inject(PlatformApi);

  readonly filter = listFilter();

  readonly kits = resource({
    params: () => this.filter(),
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

  readonly filter = listFilter();

  readonly templates = resource({
    params: () => this.filter(),
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

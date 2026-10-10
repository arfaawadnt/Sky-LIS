import { Injectable, inject, resource, signal } from '@angular/core';

import type { CreateServiceRequest, ServiceMemberItem, ServiceMemberKind, UpdateServiceRequest } from '../../../core/api/catalogue-a.contracts';
import { mutation } from '../../../core/state/mutation';
import { CatalogueAApi } from '../data-a/catalogue-a.api';

/** A member candidate from the lookups (tests for tests/profiles/cultures/text reports, services for packages). */
export interface MemberCandidate {
  readonly kind: ServiceMemberKind;
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: string;
}

/**
 * The service editor (CP-40): the loaded detail, the writes (create with members, general update, member replace,
 * activation) and the member picker over the lookup endpoints. Provided at the editor route.
 */
@Injectable()
export class ServiceEditorFacade {
  private readonly api = inject(CatalogueAApi);

  /** The service being edited, or null for a new one. */
  readonly id = signal<string | null>(null);

  readonly detail = resource({
    params: () => this.id() ?? undefined,
    loader: ({ params }) => this.api.getService(params),
  });

  readonly pickerQuery = signal<{ readonly kind: ServiceMemberKind; readonly search: string } | null>(null);

  readonly candidates = resource({
    params: () => this.pickerQuery() ?? undefined,
    loader: async ({ params, abortSignal }): Promise<readonly MemberCandidate[]> => {
      if (params.kind === 'Test') {
        const page = await this.api.lookupTests(params.search, null, abortSignal);
        return page.items.map((item) => ({ kind: 'Test', id: item.id, code: item.code, nameEn: item.nameEn, nameAr: item.nameAr, type: item.type }));
      }

      const page = await this.api.lookupServices(params.search, null, abortSignal);
      return page.items
        .filter((item) => item.type !== 'Package' && item.type !== 'Additional')
        .map((item) => ({ kind: 'Service', id: item.id, code: item.code, nameEn: item.nameEn, nameAr: item.nameAr, type: item.type }));
    },
  });

  readonly create = mutation((request: CreateServiceRequest) => this.api.createService(request));

  readonly update = mutation((input: { readonly id: string; readonly request: UpdateServiceRequest }) => this.api.updateService(input.id, input.request));

  readonly replaceMembers = mutation((input: { readonly id: string; readonly members: readonly ServiceMemberItem[] }) => this.api.replaceServiceMembers(input.id, input.members));

  readonly setActive = mutation((input: { readonly id: string; readonly active: boolean }) => this.api.setServiceActive(input.id, input.active), {
    then: () => this.detail.reload(),
  });

  /** Opens a record; the detail is re-read even for the id already loaded, because the facade outlives the page (UAT D-14). */
  load(id: string | null): void {
    this.id.set(id);
    this.detail.reload();
    this.candidates.reload();
  }

  search(kind: ServiceMemberKind, search: string): void {
    this.pickerQuery.set({ kind, search });
  }
}

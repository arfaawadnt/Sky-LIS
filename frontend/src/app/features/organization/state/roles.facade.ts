import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { CreateRoleRequest, ResultRight, RoleListFilter, SpecialRights, UpdateRoleRequest } from '../../../core/api/organization.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { OrganizationApi } from '../data/organization.api';

/** The roles register (CP-37, SA-06): filter in a signal, the list as a resource, activation as a mutation. Provided at the route. */
@Injectable()
export class RolesFacade {
  private readonly api = inject(OrganizationApi);

  readonly filter = signal<RoleListFilter>({ search: '', isActive: null, page: 1, pageSize: 50 });

  readonly list = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listRoles(params, abortSignal),
  });

  readonly setActive = mutation((input: { id: string; active: boolean }) => this.api.setRoleActive(input.id, input.active), { then: () => this.list.reload() });

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setStatus(isActive: boolean | null): void {
    this.filter.update((current) => ({ ...current, isActive, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** Re-reads the roles when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.list.reload();
  }
}

/**
 * One role in the editor (CP-37): the detail, the permission catalogue, the clinical groups and branches the result
 * rights refer to, and one mutation per resource of the role (general data, permissions, special rights, result
 * rights, activation). Saving one tab never touches another (each is its own endpoint and audit entry).
 */
@Injectable()
export class RoleEditorFacade {
  private readonly api = inject(OrganizationApi);

  /** null = a new role. */
  readonly roleId = signal<string | null>(null);

  readonly detail = resource({
    params: () => this.roleId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.getRole(params, abortSignal),
  });

  readonly catalogue = resource({ loader: ({ abortSignal }) => this.api.permissionCatalogue(abortSignal) });

  readonly clinicalGroups = resource({ loader: ({ abortSignal }) => this.api.clinicalGroups(abortSignal) });

  readonly branches = resource({ loader: ({ abortSignal }) => this.api.activeBranches(abortSignal) });

  readonly role = computed(() => (this.detail.hasValue() ? this.detail.value() : null));

  readonly create = mutation((request: CreateRoleRequest) => this.api.createRole(request));

  readonly update = mutation((request: UpdateRoleRequest) => this.api.updateRole(this.requireId(), request), { then: () => this.detail.reload() });

  readonly savePermissions = mutation((keys: readonly string[]) => this.api.setRolePermissions(this.requireId(), keys), { then: () => this.detail.reload() });

  readonly saveSpecialRights = mutation((rights: SpecialRights) => this.api.setRoleSpecialRights(this.requireId(), rights), { then: () => this.detail.reload() });

  readonly saveResultRights = mutation((rights: readonly ResultRight[]) => this.api.setRoleResultRights(this.requireId(), rights), { then: () => this.detail.reload() });

  readonly setActive = mutation((active: boolean) => this.api.setRoleActive(this.requireId(), active), { then: () => this.detail.reload() });

  /** Re-reads the role and its lookups when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.detail.reload();
    this.catalogue.reload();
    this.clinicalGroups.reload();
    this.branches.reload();
  }

  private requireId(): string {
    const id = this.roleId();
    if (id === null) {
      throw new Error('The role is not saved yet.');
    }

    return id;
  }
}

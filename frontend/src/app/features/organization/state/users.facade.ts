import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { CreateUserRequest, ResetUserPasswordRequest, SetUserOverridesRequest, UpdateUserRequest, UserListFilter, UserStatus } from '../../../core/api/organization.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { SETTING_KEYS } from '../../../core/api/settings.contracts';
import { mutation } from '../../../core/state/mutation';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { sortParams } from '../../../shared/forms/paging';
import { type TableSort, localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { OrganizationApi } from '../data/organization.api';

/** The minimum of the password policy when the tenant parameter cannot be read (decision of 2026-10-09: default 8). */
export const DEFAULT_PASSWORD_MIN_LENGTH = 8;

/** The server key of each sortable column of the users table (B-03): the name sorts by the language on screen. */
const USER_SORT_KEYS: Readonly<Record<string, (lang: string) => string>> = { name: localizedSortKey('name'), username: () => 'username' };

/** The users register (CP-38, SA-07): search, role and status filters, a server-side sort, paging. Provided at the route. */
@Injectable()
export class UsersFacade {
  private readonly api = inject(OrganizationApi);
  private readonly i18n = inject(I18nService);

  readonly filter = signal<UserListFilter>({ search: '', roleId: null, status: null, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<UserListFilter>(() => {
    const sort = this.sort();
    const key = sort === null ? undefined : USER_SORT_KEYS[sort.key];
    return { ...this.filter(), ...sortParams(sort !== null && key ? { key: key(this.i18n.lang()), direction: sort.direction } : null) };
  });

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.listUsers(params, abortSignal),
  });

  readonly roles = resource({ loader: ({ abortSignal }) => this.api.activeRoles(abortSignal).catch(() => []) });

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setRole(roleId: string | null): void {
    this.filter.update((current) => ({ ...current, roleId, page: 1 }));
  }

  setStatus(status: UserStatus | null): void {
    this.filter.update((current) => ({ ...current, status, page: 1 }));
  }

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** Re-reads the users and the role filter when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.list.reload();
    this.roles.reload();
  }
}

/**
 * One user in the editor (CP-38, SA-07): the account with its effective privileges, the pickers (active roles and
 * branches, the permission catalogue) and one mutation per administrative action. Every mutation reloads the detail,
 * so the status, the modified flag and the effective privileges always show what the server resolved.
 */
@Injectable()
export class UserEditorFacade {
  private readonly api = inject(OrganizationApi);
  private readonly permissions = inject(PermissionsService);

  /** null = a new user. */
  readonly userId = signal<string | null>(null);

  readonly detail = resource({
    params: () => this.userId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.getUser(params, abortSignal),
  });

  readonly roles = resource({ loader: ({ abortSignal }) => this.api.activeRoles(abortSignal).catch(() => []) });

  readonly branches = resource({ loader: ({ abortSignal }) => this.api.activeBranches(abortSignal).catch(() => []) });

  /** The dial codes of the mobile field (LIS-CR-007 C-05). */
  readonly mobileCountries = resource({ loader: () => this.api.mobileCountries() });

  readonly catalogue = resource({ loader: ({ abortSignal }) => this.api.permissionCatalogue(abortSignal).catch(() => ({ modules: [], actions: [] })) });

  /** The tenant's password minimum, read when the actor may read the parameters (else the default). */
  readonly passwordMinLength = resource({
    loader: async ({ abortSignal }) => {
      if (!this.permissions.can('settings.view')) {
        return DEFAULT_PASSWORD_MIN_LENGTH;
      }

      const response = await this.api.settings(abortSignal).catch(() => ({ settings: [] }));
      const value = Number(response.settings.find((setting) => setting.key === SETTING_KEYS.passwordMinLength)?.value);
      return Number.isInteger(value) && value > 0 ? value : DEFAULT_PASSWORD_MIN_LENGTH;
    },
  });

  readonly user = computed(() => (this.detail.hasValue() ? this.detail.value() : null));

  readonly create = mutation((request: CreateUserRequest) => this.api.createUser(request));

  readonly update = mutation((request: UpdateUserRequest) => this.api.updateUser(this.requireId(), request), { then: () => this.detail.reload() });

  readonly assignRole = mutation((roleId: string) => this.api.assignUserRole(this.requireId(), roleId), { then: () => this.detail.reload() });

  readonly assignBranches = mutation((choice: { readonly branchIds: readonly string[]; readonly allBranches: boolean }) => this.api.assignUserBranches(this.requireId(), choice.branchIds, choice.allBranches), { then: () => this.detail.reload() });

  readonly setOverrides = mutation((request: SetUserOverridesRequest) => this.api.setUserOverrides(this.requireId(), request), { then: () => this.detail.reload() });

  readonly restore = mutation(() => this.api.restoreUser(this.requireId()), { then: () => this.detail.reload() });

  readonly setLocked = mutation((locked: boolean) => this.api.setUserLocked(this.requireId(), locked), { then: () => this.detail.reload() });

  readonly resetPassword = mutation((request: ResetUserPasswordRequest) => this.api.resetUserPassword(this.requireId(), request), { then: () => this.detail.reload() });

  readonly setActive = mutation((active: boolean) => this.api.setUserActive(this.requireId(), active), { then: () => this.detail.reload() });

  /** Re-reads the user and its lookups when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.detail.reload();
    this.roles.reload();
    this.branches.reload();
    this.catalogue.reload();
    this.passwordMinLength.reload();
  }

  private requireId(): string {
    const id = this.userId();
    if (id === null) {
      throw new Error('The user is not saved yet.');
    }

    return id;
  }
}

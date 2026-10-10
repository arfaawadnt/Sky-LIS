import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { CreateUserRequest, ResetUserPasswordRequest, SetUserOverridesRequest, UpdateUserRequest, UserListFilter, UserStatus } from '../../../core/api/organization.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { SETTING_KEYS } from '../../../core/api/settings.contracts';
import { mutation } from '../../../core/state/mutation';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { OrganizationApi } from '../data/organization.api';

/** The minimum of the password policy when the tenant parameter cannot be read (decision of 2026-10-09: default 8). */
export const DEFAULT_PASSWORD_MIN_LENGTH = 8;

/** The users register (CP-38, SA-07): search, role and status filters, paging. Provided at the route. */
@Injectable()
export class UsersFacade {
  private readonly api = inject(OrganizationApi);

  readonly filter = signal<UserListFilter>({ search: '', roleId: null, status: null, page: 1, pageSize: 50 });

  readonly list = resource({
    params: () => this.filter(),
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

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
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

  readonly assignBranches = mutation((branchIds: readonly string[]) => this.api.assignUserBranches(this.requireId(), branchIds), { then: () => this.detail.reload() });

  readonly setOverrides = mutation((request: SetUserOverridesRequest) => this.api.setUserOverrides(this.requireId(), request), { then: () => this.detail.reload() });

  readonly restore = mutation(() => this.api.restoreUser(this.requireId()), { then: () => this.detail.reload() });

  readonly setLocked = mutation((locked: boolean) => this.api.setUserLocked(this.requireId(), locked), { then: () => this.detail.reload() });

  readonly resetPassword = mutation((request: ResetUserPasswordRequest) => this.api.resetUserPassword(this.requireId(), request), { then: () => this.detail.reload() });

  readonly setActive = mutation((active: boolean) => this.api.setUserActive(this.requireId(), active), { then: () => this.detail.reload() });

  private requireId(): string {
    const id = this.userId();
    if (id === null) {
      throw new Error('The user is not saved yet.');
    }

    return id;
  }
}

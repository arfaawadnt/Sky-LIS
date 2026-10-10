import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  type BranchListFilter,
  type BranchListItem,
  type CreateBranchRequest,
  type CreateRoleRequest,
  type CreateUserRequest,
  type CreatedResource,
  type LookupOption,
  type MyPrivileges,
  ORGANIZATION_API,
  type Paged,
  type PermissionCatalogue,
  type ResetUserPasswordRequest,
  type ResultRight,
  type RoleDetail,
  type RoleListFilter,
  type RoleListItem,
  type SelectableBranchesResponse,
  type SetUserOverridesRequest,
  type SpecialRights,
  type UpdateBranchRequest,
  type UpdateRoleRequest,
  type UpdateUserRequest,
  type UserDetail,
  type UserListFilter,
  type UserListItem,
} from '../../../core/api/organization.contracts';
import { emptyPage } from '../../../core/api/paging';
import type { SettingValueItem, TenantSettingsResponse } from '../../../core/api/settings.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** Typed calls of the module; URLs are built with `HttpParams` and encoded ids, never by string concatenation of input (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class OrganizationApi {
  private readonly http = inject(HttpClient);

  private countriesCache: Promise<readonly LookupOption[]> | null = null;
  private governoratesCache: Promise<readonly LookupOption[]> | null = null;

  // ---- branches ----------------------------------------------------------------------------------------------

  listBranches(filter: BranchListFilter, signal?: AbortSignal): Promise<Paged<BranchListItem>> {
    const params = toHttpParams({ search: filter.search, isActive: filter.isActive, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<BranchListItem>>(ORGANIZATION_API.branches, { params }), { defaultValue: emptyPage<BranchListItem>() }).then(abortable(signal));
  }

  /** Every active branch (pickers of the user and role editors). */
  async activeBranches(signal?: AbortSignal): Promise<readonly BranchListItem[]> {
    const page = await this.listBranches({ search: '', isActive: true, page: 1, pageSize: 200 }, signal);
    return page.items;
  }

  createBranch(request: CreateBranchRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(ORGANIZATION_API.branches, request));
  }

  updateBranch(id: string, request: UpdateBranchRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${ORGANIZATION_API.branches}/${encodeURIComponent(id)}`, request), { defaultValue: undefined });
  }

  setBranchActive(id: string, active: boolean): Promise<void> {
    return this.post(`${ORGANIZATION_API.branches}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`);
  }

  selectableBranches(signal?: AbortSignal): Promise<SelectableBranchesResponse> {
    return firstValueFrom(this.http.get<SelectableBranchesResponse>(ORGANIZATION_API.selectableBranches), { defaultValue: { currentBranchId: null, branches: [] } }).then(abortable(signal));
  }

  // ---- roles -------------------------------------------------------------------------------------------------

  listRoles(filter: RoleListFilter, signal?: AbortSignal): Promise<Paged<RoleListItem>> {
    const params = toHttpParams({ search: filter.search, isActive: filter.isActive, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<RoleListItem>>(ORGANIZATION_API.roles, { params }), { defaultValue: emptyPage<RoleListItem>() }).then(abortable(signal));
  }

  /** Active roles for the user editor's role picker. */
  async activeRoles(signal?: AbortSignal): Promise<readonly RoleListItem[]> {
    const page = await this.listRoles({ search: '', isActive: true, page: 1, pageSize: 200 }, signal);
    return page.items;
  }

  getRole(id: string, signal?: AbortSignal): Promise<RoleDetail> {
    return firstValueFrom(this.http.get<RoleDetail>(`${ORGANIZATION_API.roles}/${encodeURIComponent(id)}`)).then(abortable(signal));
  }

  createRole(request: CreateRoleRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(ORGANIZATION_API.roles, request));
  }

  updateRole(id: string, request: UpdateRoleRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${ORGANIZATION_API.roles}/${encodeURIComponent(id)}`, request), { defaultValue: undefined });
  }

  setRolePermissions(id: string, permissionKeys: readonly string[]): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${ORGANIZATION_API.roles}/${encodeURIComponent(id)}/permissions`, { permissionKeys }), { defaultValue: undefined });
  }

  setRoleSpecialRights(id: string, rights: SpecialRights): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${ORGANIZATION_API.roles}/${encodeURIComponent(id)}/special-rights`, rights), { defaultValue: undefined });
  }

  setRoleResultRights(id: string, rights: readonly ResultRight[]): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${ORGANIZATION_API.roles}/${encodeURIComponent(id)}/result-rights`, { rights }), { defaultValue: undefined });
  }

  setRoleActive(id: string, active: boolean): Promise<void> {
    return this.post(`${ORGANIZATION_API.roles}/${encodeURIComponent(id)}/${active ? 'activate' : 'deactivate'}`);
  }

  permissionCatalogue(signal?: AbortSignal): Promise<PermissionCatalogue> {
    return firstValueFrom(this.http.get<PermissionCatalogue>(ORGANIZATION_API.permissionCatalogue), { defaultValue: { modules: [], actions: [] } }).then(abortable(signal));
  }

  // ---- users -------------------------------------------------------------------------------------------------

  listUsers(filter: UserListFilter, signal?: AbortSignal): Promise<Paged<UserListItem>> {
    const params = toHttpParams({ search: filter.search, roleId: filter.roleId, status: filter.status, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<UserListItem>>(ORGANIZATION_API.users, { params }), { defaultValue: emptyPage<UserListItem>() }).then(abortable(signal));
  }

  getUser(id: string, signal?: AbortSignal): Promise<UserDetail> {
    return firstValueFrom(this.http.get<UserDetail>(`${ORGANIZATION_API.users}/${encodeURIComponent(id)}`)).then(abortable(signal));
  }

  createUser(request: CreateUserRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(ORGANIZATION_API.users, request));
  }

  updateUser(id: string, request: UpdateUserRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${ORGANIZATION_API.users}/${encodeURIComponent(id)}`, request), { defaultValue: undefined });
  }

  assignUserRole(id: string, roleId: string): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.user(id)}/role`, { roleId }), { defaultValue: undefined });
  }

  assignUserBranches(id: string, branchIds: readonly string[]): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.user(id)}/branches`, { branchIds }), { defaultValue: undefined });
  }

  setUserOverrides(id: string, request: SetUserOverridesRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.user(id)}/overrides`, request), { defaultValue: undefined });
  }

  restoreUser(id: string): Promise<void> {
    return this.post(`${this.user(id)}/restore`);
  }

  setUserLocked(id: string, locked: boolean): Promise<void> {
    return this.post(`${this.user(id)}/${locked ? 'lock' : 'unlock'}`);
  }

  resetUserPassword(id: string, request: ResetUserPasswordRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.user(id)}/reset-password`, request), { defaultValue: undefined });
  }

  setUserActive(id: string, active: boolean): Promise<void> {
    return this.post(`${this.user(id)}/${active ? 'reactivate' : 'deactivate'}`);
  }

  myPrivileges(signal?: AbortSignal): Promise<MyPrivileges> {
    return firstValueFrom(this.http.get<MyPrivileges>(ORGANIZATION_API.myPrivileges)).then(abortable(signal));
  }

  // ---- parameters (CP-57, foundation settings API) -----------------------------------------------------------

  settings(signal?: AbortSignal): Promise<TenantSettingsResponse> {
    return firstValueFrom(this.http.get<TenantSettingsResponse>(ORGANIZATION_API.settings), { defaultValue: { settings: [] } }).then(abortable(signal));
  }

  saveSettings(settings: readonly SettingValueItem[]): Promise<TenantSettingsResponse> {
    return firstValueFrom(this.http.put<TenantSettingsResponse>(ORGANIZATION_API.settings, { settings }));
  }

  // ---- lookups of other modules ------------------------------------------------------------------------------

  /** Platform countries (agent `plat`); empty when the list is not readable from this portal. Cached for the session. */
  countries(): Promise<readonly LookupOption[]> {
    this.countriesCache ??= this.lookup(ORGANIZATION_API.countries, {});
    return this.countriesCache;
  }

  /** Every governorate with its country as parent (one call, filtered client-side). */
  governorates(): Promise<readonly LookupOption[]> {
    this.governoratesCache ??= this.lookup(ORGANIZATION_API.governorates, {});
    return this.governoratesCache;
  }

  /** Active clinical groups of the tenant's catalogue (agent `cat`), for the result-rights grid. */
  clinicalGroups(signal?: AbortSignal): Promise<readonly LookupOption[]> {
    return this.lookup(ORGANIZATION_API.clinicalGroups, { isActive: true }).then(abortable(signal));
  }

  private user(id: string): string {
    return `${ORGANIZATION_API.users}/${encodeURIComponent(id)}`;
  }

  private post(url: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(url, null), { defaultValue: undefined });
  }

  private lookup(url: string, extra: Readonly<Record<string, string | number | boolean>>): Promise<readonly LookupOption[]> {
    const params = new HttpParams({ fromObject: { ...extra, page: 1, pageSize: 200 } });
    return firstValueFrom(this.http.get<unknown>(url, { params }), { defaultValue: [] })
      .then((body) => itemsOf(body).map(toLookup).filter((item): item is LookupOption => item !== null))
      .catch((): readonly LookupOption[] => []);
  }
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** A list endpoint may answer an array or a `Paged<T>`; anything else is empty. */
export function itemsOf(body: unknown): readonly Readonly<Record<string, unknown>>[] {
  const items: unknown = Array.isArray(body) ? body : isRecord(body) ? body['items'] : null;
  return Array.isArray(items) ? items.filter(isRecord) : [];
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

/** Normalises a reference row (`id`, `code`/`isoCode`, `nameEn`, `nameAr`, `countryId`); inactive rows are dropped. */
export function toLookup(value: Readonly<Record<string, unknown>>): LookupOption | null {
  const id = text(value['id']);
  const nameEn = text(value['nameEn']) ?? text(value['name']);
  if (id === null || nameEn === null || value['isActive'] === false) {
    return null;
  }

  return { id, code: text(value['code']) ?? text(value['isoCode']), nameEn, nameAr: text(value['nameAr']) ?? nameEn, parentId: text(value['countryId']) };
}

function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { NO_SPECIAL_RIGHTS, ORGANIZATION_API, type Paged, type UserDetail, type UserListItem } from '../../../core/api/organization.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { DEFAULT_PASSWORD_MIN_LENGTH, UserEditorFacade, UsersFacade } from './users.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const mona: UserDetail = {
  userId: 'user-1',
  membershipId: 'membership-1',
  username: 'mona.adel',
  fullNameEn: 'Mona Adel',
  fullNameAr: 'منى عادل',
  email: 'mona@lab.example',
  mobile: '+201000000001',
  employeeCode: 'MONA.ADEL',
  designation: 'Chemist',
  shifts: ['Morning'],
  preferredLanguage: 'en',
  roleId: 'role-1',
  roleNameEn: 'Reception',
  roleNameAr: 'الاستقبال',
  branchIds: [],
  status: 'Active',
  isActive: true,
  isLocked: false,
  lockoutEndUtc: null,
  mustChangePassword: false,
  passwordExpiresAtUtc: null,
  passwordChangedAtUtc: '2026-10-01T08:00:00Z',
  signatureFileId: null,
  isPrimaryAdministrator: false,
  isModified: false,
  belongsToOtherTenants: false,
  overrides: { addedPermissionKeys: [], removedPermissionKeys: [], specialRights: null },
  effective: { permissionKeys: ['registration.view'], specialRights: NO_SPECIAL_RIGHTS, resultRights: [] },
  rolePermissionKeys: ['registration.view'],
  roleSpecialRights: NO_SPECIAL_RIGHTS,
};

describe('Users facades (CP-38)', () => {
  let backend: HttpTestingController;
  let canReadSettings = true;

  beforeEach(() => {
    canReadSettings = true;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        UsersFacade,
        UserEditorFacade,
        { provide: PermissionsService, useValue: { can: (key: string) => key !== 'settings.view' || canReadSettings } satisfies Pick<PermissionsService, 'can'> },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  function flushPickers(): void {
    backend.match((request) => request.url === ORGANIZATION_API.roles || request.url === ORGANIZATION_API.branches).forEach((request) => request.flush(page([])));
    backend.match(ORGANIZATION_API.permissionCatalogue).forEach((request) => request.flush({ modules: [], actions: [] }));
  }

  it('filters the register by search, role and status', async () => {
    const facade = TestBed.inject(UsersFacade);
    await runEffects();
    backend.expectOne((request) => request.url === ORGANIZATION_API.users).flush(page<UserListItem>([]));
    backend.expectOne((request) => request.url === ORGANIZATION_API.roles).flush(page([]));

    facade.setSearch('mona');
    facade.setRole('role-1');
    facade.setStatus('Locked');
    await runEffects();

    const filtered = backend.expectOne((request) => request.url === ORGANIZATION_API.users);
    expect(filtered.request.params.get('search')).toBe('mona');
    expect(filtered.request.params.get('roleId')).toBe('role-1');
    expect(filtered.request.params.get('status')).toBe('Locked');
    filtered.flush(page<UserListItem>([]));
    backend.verify();
  });

  it('reads the password minimum from the parameters, and falls back to 8 without the permission', async () => {
    const facade = TestBed.inject(UserEditorFacade);
    await runEffects();
    flushPickers();
    backend.expectOne(ORGANIZATION_API.settings).flush({ settings: [{ key: 'passwordMinLength', type: 'Int', value: '12', defaultValue: '8', description: '', isDefault: false, updatedAtUtc: null, updatedByUserId: null }] });
    await runEffects();

    expect(facade.passwordMinLength.value()).toBe(12);

    canReadSettings = false;
    facade.passwordMinLength.reload();
    await runEffects();
    expect(facade.passwordMinLength.value()).toBe(DEFAULT_PASSWORD_MIN_LENGTH);
    backend.verify();
  });

  it('creates a user with the password in the body (sealed by the envelope interceptor) and posts the access actions', async () => {
    const facade = TestBed.inject(UserEditorFacade);
    await runEffects();
    flushPickers();
    backend.match(ORGANIZATION_API.settings).forEach((request) => request.flush({ settings: [] }));

    const creating = facade.create.execute({
      username: 'mona.adel',
      password: 'a long secret',
      mustChangePassword: true,
      fullNameEn: 'Mona Adel',
      fullNameAr: 'منى عادل',
      email: 'mona@lab.example',
      mobile: null,
      designation: null,
      shifts: [],
      roleId: 'role-1',
      branchIds: [],
      passwordExpiresAtUtc: null,
      employeeCode: null,
      preferredLanguage: 'en',
    });
    await settle();
    const post = backend.expectOne({ method: 'POST', url: ORGANIZATION_API.users });
    expect(post.request.body).toMatchObject({ username: 'mona.adel', password: 'a long secret', roleId: 'role-1', branchIds: [] });
    post.flush({ id: 'user-1' }, { status: 201, statusText: 'Created' });
    expect(await creating).toEqual({ id: 'user-1' });

    facade.userId.set('user-1');
    await runEffects();
    backend.expectOne(`${ORGANIZATION_API.users}/user-1`).flush(mona);
    await runEffects();

    const resetting = facade.resetPassword.execute({ temporaryPassword: 'temporary pass', mustChangePassword: true });
    await settle();
    const reset = backend.expectOne({ method: 'POST', url: `${ORGANIZATION_API.users}/user-1/reset-password` });
    expect(reset.request.body).toEqual({ temporaryPassword: 'temporary pass', mustChangePassword: true });
    reset.flush(null, { status: 204, statusText: 'No Content' });
    await resetting;
    await runEffects();
    backend.expectOne(`${ORGANIZATION_API.users}/user-1`).flush({ ...mona, mustChangePassword: true });

    const overriding = facade.setOverrides.execute({ addedPermissionKeys: ['patients.view'], removedPermissionKeys: [], specialRights: null });
    await settle();
    const put = backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.users}/user-1/overrides` });
    expect(put.request.body).toEqual({ addedPermissionKeys: ['patients.view'], removedPermissionKeys: [], specialRights: null });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await overriding;
    await runEffects();
    backend.expectOne(`${ORGANIZATION_API.users}/user-1`).flush({ ...mona, isModified: true });

    const locking = facade.setLocked.execute(true);
    await settle();
    backend.expectOne({ method: 'POST', url: `${ORGANIZATION_API.users}/user-1/lock` }).flush(null, { status: 204, statusText: 'No Content' });
    await locking;
    await runEffects();
    backend.match(() => true).forEach((request) => request.flush(mona));
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { NO_SPECIAL_RIGHTS, ORGANIZATION_API, type UserDetail } from '../../../core/api/organization.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { UserEditorFacade } from '../state/users.facade';
import { UserEditorPage, fromDateInput, parseShifts, toDateInput } from './user-editor.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const page = { items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 };

const mona: UserDetail = {
  userId: 'user-1',
  membershipId: 'membership-1',
  username: 'mona.adel',
  fullNameEn: 'Mona Adel',
  fullNameAr: 'منى عادل',
  email: 'mona@lab.example',
  mobile: null,
  employeeCode: 'MONA.ADEL',
  designation: null,
  shifts: ['Morning', 'Night'],
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
  isModified: true,
  belongsToOtherTenants: false,
  overrides: { addedPermissionKeys: ['patients.view'], removedPermissionKeys: [], specialRights: null },
  effective: { permissionKeys: ['patients.view', 'registration.view'], specialRights: NO_SPECIAL_RIGHTS, resultRights: [] },
  rolePermissionKeys: ['registration.view'],
  roleSpecialRights: NO_SPECIAL_RIGHTS,
};

describe('UserEditorPage (CP-38)', () => {
  let backend: HttpTestingController;
  const currentUserId = signal('admin-1');

  beforeEach(async () => {
    currentUserId.set('admin-1');
    await TestBed.configureTestingModule({
      imports: [UserEditorPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        UserEditorFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        {
          provide: AuthStore,
          useValue: {
            currentUser: () => ({ id: currentUserId(), displayName: 'Admin', language: 'en', scope: 'full' }),
            currentTenant: () => 'tenant-1',
            hasPermission: () => true,
          },
        },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function flush(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  function flushPickers(): void {
    backend.match((request) => request.url === ORGANIZATION_API.roles || request.url === ORGANIZATION_API.branches).forEach((request) => request.flush(page));
    backend.match(ORGANIZATION_API.permissionCatalogue).forEach((request) =>
      request.flush({ modules: [{ key: 'patients', displayName: 'Patients', actions: ['view'], platformOnly: false }, { key: 'registration', displayName: 'Visits', actions: ['view'], platformOnly: false }], actions: ['view'] }),
    );
    backend.match(ORGANIZATION_API.settings).forEach((request) => request.flush({ settings: [] }));
  }

  it('creates a user: refuses two different passwords, then posts the profile with the password', async () => {
    const fixture = TestBed.createComponent(UserEditorPage);
    await flush();
    flushPickers();
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    const set = (name: string, value: string): void => {
      const control = host.querySelector<HTMLInputElement>(`[formcontrolname="${name}"]`);
      if (control) {
        control.value = value;
        control.dispatchEvent(new Event('input'));
      }
    };
    set('username', 'mona.adel');
    set('password', 'a long secret');
    set('confirm', 'another secret');
    set('email', 'mona@lab.example');
    host.querySelectorAll<HTMLInputElement>('app-localized-text-input input').forEach((input, index) => {
      input.value = index === 0 ? 'Mona Adel' : 'منى عادل';
      input.dispatchEvent(new Event('input'));
    });
    set('shifts', 'Morning, Night');
    await flush();

    const submit = host.querySelector<HTMLButtonElement>('form button[type="submit"]');
    submit?.click();
    await flush();
    expect(host.textContent).toContain('organization.reset.mismatch');
    backend.expectNone({ method: 'POST', url: ORGANIZATION_API.users });

    set('confirm', 'a long secret');
    submit?.click();
    await flush();
    const post = backend.expectOne({ method: 'POST', url: ORGANIZATION_API.users });
    expect(post.request.body).toMatchObject({ username: 'mona.adel', password: 'a long secret', email: 'mona@lab.example', shifts: ['Morning', 'Night'], mustChangePassword: true, branchIds: [] });
  });

  it('shows the overrides against the role and hides access actions on the actor own account', async () => {
    const fixture = TestBed.createComponent(UserEditorPage);
    fixture.componentRef.setInput('userId', 'user-1');
    await flush();
    flushPickers();
    backend.expectOne(`${ORGANIZATION_API.users}/user-1`).flush(mona);
    await flush();

    const component = fixture.componentInstance as unknown as { onTab(id: string): void };
    component.onTab('privileges');
    await flush();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.chip.added')?.textContent).toContain('patients.view');
    expect(host.querySelectorAll('td.modified').length).toBe(1);

    currentUserId.set('user-1');
    await flush();
    expect(host.textContent).toContain('organization.users.selfNotice');
    expect(host.querySelector('app-privilege-matrix input[type="checkbox"]:not([disabled])')).toBeNull();
  });

  it('converts dates and shifts for the form', () => {
    expect(parseShifts(' Morning, ,Night ')).toEqual(['Morning', 'Night']);
    expect(fromDateInput('')).toBeNull();
    const iso = fromDateInput('2027-01-31');
    expect(iso).not.toBeNull();
    expect(toDateInput(iso)).toBe('2027-01-31');
  });
});

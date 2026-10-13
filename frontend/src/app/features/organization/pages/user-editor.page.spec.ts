import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { NO_SPECIAL_RIGHTS, ORGANIZATION_API, type UserDetail } from '../../../core/api/organization.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
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
  let confirmAnswer = true;

  beforeEach(async () => {
    currentUserId.set('admin-1');
    confirmAnswer = true;
    await TestBed.configureTestingModule({
      imports: [UserEditorPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        UserEditorFacade,
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(confirmAnswer) } satisfies Pick<ConfirmService, 'confirm'> },
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
    expect(post.request.body).toMatchObject({ username: 'mona.adel', password: 'a long secret', email: 'mona@lab.example', shifts: ['Morning', 'Night'], mustChangePassword: true, branchIds: [], allBranches: true });
  });

  /** Opens the "new user" page and fills a valid profile. */
  async function newUserFilled(): Promise<{ host: HTMLElement; set(name: string, value: string): void; submit(): Promise<void> }> {
    const fixture = TestBed.createComponent(UserEditorPage);
    await flush();
    flushPickers();
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);   // focus needs a connected element
    const set = (name: string, value: string): void => {
      const control = host.querySelector<HTMLInputElement>(`[formcontrolname="${name}"]`);
      if (control) {
        control.value = value;
        control.dispatchEvent(new Event('input'));
      }
    };
    set('username', 'mona.adel');
    set('password', 'a long secret');
    set('confirm', 'a long secret');
    set('email', 'mona@lab.example');
    set('mobile', '+201001234567');
    host.querySelectorAll<HTMLInputElement>('app-localized-text-input input').forEach((input, index) => {
      input.value = index === 0 ? 'Mona Adel' : 'منى عادل';
      input.dispatchEvent(new Event('input'));
    });
    await flush();
    const submit = async (): Promise<void> => {
      host.querySelector<HTMLButtonElement>('form button[type="submit"]')?.click();
      await flush();
    };
    return { host, set, submit };
  }

  const banner = (host: HTMLElement): string[] => [...host.querySelectorAll('.editor > p.problem[role="alert"]')].map((line) => line.textContent ?? '');

  for (const [code, field] of [['ORG-102', 'username'], ['ORG-103', 'email'], ['ORG-104', 'mobile'], ['ORG-105', 'employeeCode']] as const) {
    it(`O-6: a duplicate refusal ${code} is shown under the ${field} field, not in a banner, and the field takes the focus`, async () => {
      const { host, submit } = await newUserFilled();
      await submit();
      backend
        .expectOne({ method: 'POST', url: ORGANIZATION_API.users })
        .flush({ type: 'about:blank', title: 'Conflict', status: 409, code, traceId: null, correlationId: null, errors: null }, { status: 409, statusText: 'Conflict' });
      await flush();

      const input = host.querySelector<HTMLInputElement>(`[formcontrolname="${field}"]`);
      expect(input?.getAttribute('aria-invalid')).toBe('true');
      expect(input?.closest('app-form-field')?.querySelector('[role="alert"]')).not.toBeNull();
      expect(banner(host)).toEqual([]);
      expect(document.activeElement).toBe(input);
      host.remove();
    });
  }

  it('O-6: any other refusal stays in the banner', async () => {
    const { host, submit } = await newUserFilled();
    await submit();
    backend
      .expectOne({ method: 'POST', url: ORGANIZATION_API.users })
      .flush({ type: 'about:blank', title: 'Refused', status: 422, code: 'ORG-018', traceId: null, correlationId: null, errors: null }, { status: 422, statusText: 'Unprocessable Entity' });
    await flush();

    expect(banner(host).length).toBe(1);
    host.remove();
  });

  it('a blocked save shows the notice, as the drawer editors do, and focuses the first invalid field', async () => {
    const { host, set, submit } = await newUserFilled();
    set('username', '');
    set('email', 'not-an-email');
    await flush();
    await submit();

    backend.expectNone({ method: 'POST', url: ORGANIZATION_API.users });
    const notice = host.querySelector('form p.problem[role="alert"]');
    expect(notice?.textContent).toContain('shared.masterData.invalid');
    expect(document.activeElement).toBe(host.querySelector('[formcontrolname="username"]'));

    set('username', 'mona.adel');
    set('email', 'mona@lab.example');
    await flush();
    expect(host.querySelector('form p.problem[role="alert"]')).toBeNull();   // the notice goes once the fields are corrected
    host.remove();
  });

  it('B-02: refuses to create a user restricted to no branch, then posts the chosen branch as a restricted choice', async () => {
    const fixture = TestBed.createComponent(UserEditorPage);
    await flush();
    backend.match((request) => request.url === ORGANIZATION_API.branches).forEach((request) =>
      request.flush({ ...page, items: [{ id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي', countryId: null, governorateId: null, phone: null, email: null, currency: 'EGP', isActive: true }], totalCount: 1, totalPages: 1 }),
    );
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
    set('confirm', 'a long secret');
    set('email', 'mona@lab.example');
    host.querySelectorAll<HTMLInputElement>('app-localized-text-input input').forEach((input, index) => {
      input.value = index === 0 ? 'Mona Adel' : 'منى عادل';
      input.dispatchEvent(new Event('input'));
    });
    const everyBranch = host.querySelector<HTMLInputElement>('fieldset.branches input[type="checkbox"]');
    everyBranch?.click();
    await flush();

    const submit = host.querySelector<HTMLButtonElement>('form button[type="submit"]');
    submit?.click();
    await flush();
    backend.expectNone({ method: 'POST', url: ORGANIZATION_API.users });
    expect(TestBed.inject(ToastService).toasts().map((toast) => toast.text)).toContain('organization.users.pickBranch');

    host.querySelector<HTMLInputElement>('fieldset.branches .branch-list input[type="checkbox"]')?.click();
    await flush();
    submit?.click();
    await flush();
    const post = backend.expectOne({ method: 'POST', url: ORGANIZATION_API.users });
    expect(post.request.body).toMatchObject({ branchIds: ['b1'], allBranches: false });
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

  it('validation v0.5.0 O-7: the overrides hide the Release 2 modules and a save keeps the overrides the user holds on them', async () => {
    const fixture = TestBed.createComponent(UserEditorPage);
    fixture.componentRef.setInput('userId', 'user-1');
    await flush();
    backend.match((request) => request.url === ORGANIZATION_API.roles || request.url === ORGANIZATION_API.branches).forEach((request) => request.flush(page));
    backend.match(ORGANIZATION_API.permissionCatalogue).forEach((request) =>
      request.flush({
        modules: [
          { key: 'patients', displayName: 'Patients', actions: ['view'], platformOnly: false },
          { key: 'registration', displayName: 'Visits', actions: ['view'], platformOnly: false },
          { key: 'claims', displayName: 'Payer claims', actions: ['view'], platformOnly: false },
          { key: 'quality', displayName: 'Quality control', actions: ['view'], platformOnly: false },
        ],
        actions: ['view'],
      }),
    );
    backend.match(ORGANIZATION_API.settings).forEach((request) => request.flush({ settings: [] }));
    backend.expectOne(`${ORGANIZATION_API.users}/user-1`).flush({
      ...mona,
      rolePermissionKeys: ['quality.view', 'registration.view'],
      overrides: { addedPermissionKeys: ['claims.view', 'patients.view'], removedPermissionKeys: ['quality.view'], specialRights: null },
    });
    await flush();
    (fixture.componentInstance as unknown as { onTab(id: string): void }).onTab('privileges');
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    expect([...host.querySelectorAll('app-privilege-matrix tbody th')].map((cell) => cell.textContent?.trim())).toEqual(['Patients', 'Visits']);
    expect([...host.querySelectorAll('.chip')].map((chip) => chip.textContent?.trim())).toEqual(['+ patients.view']);
    expect(host.querySelector('.kept-keys')?.textContent).toContain('organization.users.keptLaterRelease');

    host.querySelector<HTMLInputElement>('input[aria-label="Visits: view"]')?.click();
    await flush();
    [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.trim() === 'organization.users.saveOverrides')?.click();
    await flush();

    const put = backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.users}/user-1/overrides` });
    expect(put.request.body).toEqual({ addedPermissionKeys: ['claims.view', 'patients.view'], removedPermissionKeys: ['quality.view', 'registration.view'], specialRights: null });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await flush();
  });

  it('converts dates and shifts for the form', () => {
    expect(parseShifts(' Morning, ,Night ')).toEqual(['Morning', 'Night']);
    expect(fromDateInput('')).toBeNull();
    const iso = fromDateInput('2027-01-31');
    expect(iso).not.toBeNull();
    expect(toDateInput(iso)).toBe('2027-01-31');
  });

  it('saving the branches also assigns a role picked but not assigned yet (UAT B-14)', async () => {
    const fixture = TestBed.createComponent(UserEditorPage);
    fixture.componentRef.setInput('userId', 'user-1');
    await flush();
    flushPickers();
    backend.expectOne(`${ORGANIZATION_API.users}/user-1`).flush(mona);
    await flush();

    const component = fixture.componentInstance as unknown as { roleChoice: { set(value: string): void }; saveBranches(): Promise<void> };
    component.roleChoice.set('role-2');
    const saving = component.saveBranches();
    await flush();
    const role = backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.users}/user-1/role` });
    expect(role.request.body).toEqual({ roleId: 'role-2' });
    role.flush(null, { status: 204, statusText: 'No Content' });
    await flush();
    backend.match(`${ORGANIZATION_API.users}/user-1`).forEach((request) => request.flush({ ...mona, roleId: 'role-2' }));
    const branches = backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.users}/user-1/branches` });
    expect(branches.request.body).toEqual({ branchIds: [], allBranches: true });
    branches.flush(null, { status: 204, statusText: 'No Content' });
    await saving;
  });

  it('saving the branches with a declined role change keeps the branches and says the role was not assigned (UAT B-14)', async () => {
    confirmAnswer = false;
    const fixture = TestBed.createComponent(UserEditorPage);
    fixture.componentRef.setInput('userId', 'user-1');
    await flush();
    flushPickers();
    backend.expectOne(`${ORGANIZATION_API.users}/user-1`).flush(mona);
    await flush();

    const component = fixture.componentInstance as unknown as { roleChoice: { set(value: string): void }; saveBranches(): Promise<void> };
    component.roleChoice.set('role-2');
    const saving = component.saveBranches();
    await flush();
    backend.expectNone({ method: 'PUT', url: `${ORGANIZATION_API.users}/user-1/role` });
    backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.users}/user-1/branches` }).flush(null, { status: 204, statusText: 'No Content' });
    await saving;
  });
});

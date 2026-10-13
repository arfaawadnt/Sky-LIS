import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { NO_SPECIAL_RIGHTS, ORGANIZATION_API, type RoleDetail, type RoleListItem } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import organizationEn from '../../../../../public/i18n/organization.en.json';
import sharedEn from '../../../../../public/i18n/shared.en.json';
import { RoleEditorFacade, RolesFacade } from '../state/roles.facade';
import { RoleEditorPage } from './role-editor.page';
import { RolesPage } from './roles.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const emptyPage = { items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 };

const catalogue = {
  modules: [{ key: 'registration', displayName: 'Visits', actions: ['view', 'create'], platformOnly: false }],
  actions: ['view', 'create'],
};

const reception: RoleDetail = {
  id: 'role-1',
  nameEn: 'Reception',
  nameAr: 'الاستقبال',
  description: 'Front desk',
  isActive: true,
  permissionKeys: ['registration.view'],
  specialRights: NO_SPECIAL_RIGHTS,
  resultRights: [],
  memberCount: 3,
  holdsPrimaryAdministrator: false,
};

describe('Role pages (CP-37)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RoleEditorPage, RolesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        RoleEditorFacade,
        RolesFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
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

  function flushLookups(): void {
    backend.match(ORGANIZATION_API.permissionCatalogue).forEach((request) => request.flush(catalogue));
    backend.match((request) => request.url === ORGANIZATION_API.clinicalGroups || request.url === ORGANIZATION_API.branches).forEach((request) => request.flush(emptyPage));
  }

  it('creates a role only with a permission: the matrix supplies the keys', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(RoleEditorPage);
    await flush();
    flushLookups();
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    host.querySelectorAll<HTMLInputElement>('app-localized-text-input input').forEach((input, index) => {
      input.value = index === 0 ? 'Cashier' : 'أمين صندوق';
      input.dispatchEvent(new Event('input'));
    });
    host.querySelector<HTMLButtonElement>('form button[type="submit"]')?.click();
    await flush();
    backend.expectNone({ method: 'POST', url: ORGANIZATION_API.roles });
    expect(host.querySelector('app-privilege-matrix')).not.toBeNull();   // moved to the permissions tab

    host.querySelector<HTMLInputElement>('input[aria-label="Visits: view"]')?.click();
    await flush();
    [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('organization.roles.create'))?.click();
    await flush();

    const post = backend.expectOne({ method: 'POST', url: ORGANIZATION_API.roles });
    expect(post.request.body).toEqual({ nameEn: 'Cashier', nameAr: 'أمين صندوق', description: null, permissionKeys: ['registration.view'] });
    post.flush({ id: 'role-9' }, { status: 201, statusText: 'Created' });
    await flush();
    expect(navigate).toHaveBeenCalledWith(['/organization/roles', 'role-9']);
  });

  it('loads a role, shows its members and keeps deactivation unavailable while it has members', async () => {
    const fixture = TestBed.createComponent(RoleEditorPage);
    fixture.componentRef.setInput('roleId', 'role-1');
    await flush();
    flushLookups();
    backend.expectOne(`${ORGANIZATION_API.roles}/role-1`).flush(reception);
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Reception');
    const deactivate = [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('organization.deactivate'));
    expect(deactivate?.disabled).toBe(true);
  });

  it('lists roles with their counts', async () => {
    const fixture = TestBed.createComponent(RolesPage);
    await flush();
    const rows: RoleListItem[] = [{ id: 'role-1', nameEn: 'Reception', nameAr: 'الاستقبال', description: null, permissionCount: 4, memberCount: 3, isActive: true }];
    backend.expectOne((request) => request.url === ORGANIZATION_API.roles).flush({ items: rows, page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(1);
    expect(host.textContent).toContain('Reception');
  });

  it('validation v0.5.0 O-7: hides the modules of a later release and keeps the keys of them the role holds when it saves', async () => {
    const fixture = TestBed.createComponent(RoleEditorPage);
    fixture.componentRef.setInput('roleId', 'role-1');
    await flush();
    backend.match(ORGANIZATION_API.permissionCatalogue).forEach((request) =>
      request.flush({
        modules: [
          { key: 'registration', displayName: 'Visits', actions: ['view', 'create'], platformOnly: false },
          { key: 'claims', displayName: 'Payer claims', actions: ['view'], platformOnly: false },
          { key: 'quality', displayName: 'Quality control', actions: ['view', 'manage'], platformOnly: false },
        ],
        actions: ['view', 'create', 'manage'],
      }),
    );
    backend.match((request) => request.url === ORGANIZATION_API.clinicalGroups || request.url === ORGANIZATION_API.branches).forEach((request) => request.flush(emptyPage));
    backend.expectOne(`${ORGANIZATION_API.roles}/role-1`).flush({ ...reception, memberCount: 0, permissionKeys: ['claims.view', 'quality.manage', 'registration.view'] });
    await flush();
    (fixture.componentInstance as unknown as { onTab(id: string): void }).onTab('permissions');
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    const rows = [...host.querySelectorAll('app-privilege-matrix tbody th')].map((cell) => cell.textContent?.trim());
    expect(rows).toEqual(['Visits']);
    expect(host.querySelector('.kept-keys')?.textContent).toContain('organization.roles.keptLaterRelease');
    expect(host.querySelector('app-privilege-matrix .count')?.textContent).toBeTruthy();

    host.querySelector<HTMLInputElement>('input[aria-label="Visits: create"]')?.click();
    await flush();
    [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.trim() === 'organization.save')?.click();
    await flush();

    const put = backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.roles}/role-1/permissions` });
    expect(put.request.body).toEqual({ permissionKeys: ['claims.view', 'quality.manage', 'registration.create', 'registration.view'] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await flush();
  });

  /** The pages' dictionaries, served as the app serves them. */
  async function withDictionaries(run: () => Promise<void>): Promise<void> {
    vi.stubGlobal('fetch', (url: string) =>
      Promise.resolve(new Response(JSON.stringify(url.includes('/organization.en.json') ? organizationEn : url.includes('/shared.en.json') ? sharedEn : {}), { status: 200 })));
    try {
      const i18n = TestBed.inject(I18nService);
      await i18n.load('organization');
      await i18n.load('shared');
      await run();
    } finally {
      vi.unstubAllGlobals();
    }
  }

  async function openRole(fixture: ReturnType<typeof TestBed.createComponent<RoleEditorPage>>, role: RoleDetail): Promise<void> {
    fixture.componentRef.setInput('roleId', role.id);
    await flush();
    flushLookups();
    backend.expectOne(`${ORGANIZATION_API.roles}/${role.id}`).flush(role);
    await flush();
  }

  it('validation F-14: a refused save does not greet the next visit of the editor, nor another role', async () => {
    const confirm = vi.spyOn(TestBed.inject(ConfirmService), 'confirm').mockResolvedValue(true);
    await withDictionaries(async () => {
      const fixture = TestBed.createComponent(RoleEditorPage);
      await openRole(fixture, { ...reception, memberCount: 1 });
      (fixture.componentInstance as unknown as { onTab(id: string): void }).onTab('permissions');
      await flush();
      const host = fixture.nativeElement as HTMLElement;
      host.querySelector<HTMLInputElement>('app-privilege-matrix tbody td:not(.all-col) input[type="checkbox"]:not(:checked)')?.click();   // the labels are translated here
      await flush();
      [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.trim() === 'Save')?.click();
      await flush();

      // one member: the singular sentence (F-04 plurals)
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'This role has 1 member. The change applies to that user. Continue?' }));
      backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.roles}/role-1/permissions` }).flush({ code: 'ORG-033', title: 'refused', status: 403 }, { status: 403, statusText: 'Forbidden' });
      await flush();
      expect(host.querySelector('.problem[role="alert"]')).not.toBeNull();

      // another role in the same editor
      await openRole(fixture, { ...reception, id: 'role-2', nameEn: 'Cashier' });
      expect(host.querySelector('.problem[role="alert"]')).toBeNull();
      fixture.destroy();

      // the editor opened again (the route-provided facade outlives the page)
      const again = TestBed.createComponent(RoleEditorPage);
      await openRole(again, reception);
      expect((again.nativeElement as HTMLElement).querySelector('.problem[role="alert"]')).toBeNull();
    });
  });

  it('validation F-07: the matrix counter and the tab badge count only the keys the matrix shows', async () => {
    await withDictionaries(async () => {
      const fixture = TestBed.createComponent(RoleEditorPage);
      await openRole(fixture, { ...reception, permissionKeys: ['platform.manage', 'platform.view', 'registration.view', 'tenancy.view'] });
      (fixture.componentInstance as unknown as { onTab(id: string): void }).onTab('permissions');
      await flush();

      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('app-privilege-matrix .count')?.textContent?.trim()).toBe('1 of 2 permissions granted');
      expect(host.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toContain('1');
      expect(host.querySelector('[role="tab"][aria-selected="true"]')?.textContent).not.toContain('4');
    });
  });

  it('a new role refused for its fields says so and focuses the first marked field (validation F-03)', async () => {
    await withDictionaries(async () => {
      const fixture = TestBed.createComponent(RoleEditorPage);
      document.body.appendChild(fixture.nativeElement as HTMLElement);
      try {
        await flush();
        flushLookups();
        await flush();
        const host = fixture.nativeElement as HTMLElement;

        host.querySelector<HTMLButtonElement>('form button[type="submit"]')?.click();
        await flush();

        expect(host.querySelector('#role-general .problem[role="alert"]')?.textContent?.trim()).toBe('The record was not saved: correct the marked fields first.');
        expect(document.activeElement).toBe(host.querySelector('app-localized-text-input input'));
        backend.expectNone({ method: 'POST', url: ORGANIZATION_API.roles });
      } finally {
        (fixture.nativeElement as HTMLElement).remove();
      }
    });
  });

  it('the kept-keys note reads the singular for one key', async () => {
    await withDictionaries(async () => {
      const fixture = TestBed.createComponent(RoleEditorPage);
      await openRole(fixture, { ...reception, permissionKeys: ['claims.view', 'registration.view'] });
      (fixture.componentInstance as unknown as { onTab(id: string): void }).onTab('permissions');
      await flush();

      expect((fixture.nativeElement as HTMLElement).querySelector('.kept-keys')?.textContent?.trim()).toBe(
        'This role also holds 1 permission of a module of a later release; it is not shown and is kept when you save.',
      );
    });
  });
});

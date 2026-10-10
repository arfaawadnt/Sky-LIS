import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { NO_SPECIAL_RIGHTS, ORGANIZATION_API, type RoleDetail, type RoleListItem } from '../../../core/api/organization.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
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
});

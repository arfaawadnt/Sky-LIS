import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { NO_SPECIAL_RIGHTS, ORGANIZATION_API, type Paged, type RoleDetail, type RoleListItem } from '../../../core/api/organization.contracts';
import { RoleEditorFacade, RolesFacade } from './roles.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const reception: RoleDetail = {
  id: 'role-1',
  nameEn: 'Reception',
  nameAr: 'الاستقبال',
  description: null,
  isActive: true,
  permissionKeys: ['registration.view'],
  specialRights: NO_SPECIAL_RIGHTS,
  resultRights: [],
  memberCount: 2,
  holdsPrimaryAdministrator: false,
};

describe('Roles facades (CP-37)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), RolesFacade, RoleEditorFacade],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('B-03: sorts the roles on the server from page 1 by the name of the language on screen', async () => {
    const facade = TestBed.inject(RolesFacade);
    await runEffects();
    backend.expectOne((request) => request.url === ORGANIZATION_API.roles).flush(page<RoleListItem>([]));

    facade.setPage({ page: 2, pageSize: 20 });
    await runEffects();
    backend.expectOne((request) => request.url === ORGANIZATION_API.roles).flush(page<RoleListItem>([]));

    facade.setSort({ key: 'name', direction: 'desc' });
    await runEffects();
    const sorted = backend.expectOne((request) => request.url === ORGANIZATION_API.roles);
    expect([sorted.request.params.get('sort'), sorted.request.params.get('direction'), sorted.request.params.get('page')]).toEqual(['nameEn', 'desc', '1']);
    sorted.flush(page<RoleListItem>([]));

    facade.setSort(null);
    await runEffects();
    expect(backend.expectOne((request) => request.url === ORGANIZATION_API.roles).request.params.has('sort')).toBe(false);
  });

  it('lists roles for the filter and narrows to a status and a search', async () => {
    const facade = TestBed.inject(RolesFacade);
    await runEffects();
    const first = backend.expectOne((request) => request.url === ORGANIZATION_API.roles);
    expect(first.request.params.has('isActive')).toBe(false);
    first.flush(page<RoleListItem>([]));

    facade.setStatus(false);
    facade.setSearch('rec');
    await runEffects();

    const filtered = backend.expectOne((request) => request.url === ORGANIZATION_API.roles);
    expect(filtered.request.params.get('isActive')).toBe('false');
    expect(filtered.request.params.get('search')).toBe('rec');
    expect(filtered.request.params.get('page')).toBe('1');
    filtered.flush(page<RoleListItem>([]));
    backend.verify();
  });

  it('loads the role when an id is set and saves the permissions then reloads it', async () => {
    const facade = TestBed.inject(RoleEditorFacade);
    facade.roleId.set('role-1');
    await runEffects();

    backend.expectOne(`${ORGANIZATION_API.roles}/role-1`).flush(reception);
    backend.expectOne(ORGANIZATION_API.permissionCatalogue).flush({ modules: [], actions: [] });
    backend.expectOne((request) => request.url === ORGANIZATION_API.clinicalGroups).flush(page([]));
    backend.expectOne((request) => request.url === ORGANIZATION_API.branches).flush(page([]));
    await runEffects();
    expect(facade.role()?.nameEn).toBe('Reception');

    const saving = facade.savePermissions.execute(['registration.create', 'registration.view']);
    await settle();
    const put = backend.expectOne({ method: 'PUT', url: `${ORGANIZATION_API.roles}/role-1/permissions` });
    expect(put.request.body).toEqual({ permissionKeys: ['registration.create', 'registration.view'] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await saving;
    await runEffects();

    backend.expectOne(`${ORGANIZATION_API.roles}/role-1`).flush({ ...reception, permissionKeys: ['registration.create', 'registration.view'] });
    await runEffects();
    expect(facade.role()?.permissionKeys.length).toBe(2);
    backend.verify();
  });

  it('keeps the refusal code of a failed save', async () => {
    const facade = TestBed.inject(RoleEditorFacade);
    facade.roleId.set('role-1');
    await runEffects();
    backend.match(() => true).forEach((request) => request.flush(request.request.url.endsWith('role-1') ? reception : page([])));
    await runEffects();

    const saving = facade.setActive.execute(false);
    await settle();
    backend
      .expectOne(`${ORGANIZATION_API.roles}/role-1/deactivate`)
      .flush({ type: 'about:blank', title: 'The role still has members.', status: 409, code: 'ORG-011' }, { status: 409, statusText: 'Conflict' });

    expect(await saving).toBeNull();
    expect(facade.setActive.problem()).not.toBeNull();
  });
});

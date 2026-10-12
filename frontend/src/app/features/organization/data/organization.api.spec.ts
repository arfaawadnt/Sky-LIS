import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { mutation } from '../../../core/state/mutation';
import { OrganizationApi } from './organization.api';

/** UAT B-06 / C-02: an empty 204 answer is a success; `mutation()` reads a `null` result as a failure. */
describe('OrganizationApi empty answers', () => {
  let api: OrganizationApi;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(OrganizationApi);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  const cases: readonly (readonly [string, (api: OrganizationApi) => Promise<void>, string, string])[] = [
    ['update a branch', (a) => a.updateBranch('b1', { nameEn: 'Main', nameAr: 'الرئيسي' } as never), 'PUT', `${ORGANIZATION_API.branches}/b1`],
    ['deactivate a branch', (a) => a.setBranchActive('b1', false), 'POST', `${ORGANIZATION_API.branches}/b1/deactivate`],
    ['save a role', (a) => a.updateRole('r1', { nameEn: 'Lab', nameAr: 'مختبر' } as never), 'PUT', `${ORGANIZATION_API.roles}/r1`],
    ['save the role screens', (a) => a.setRolePermissions('r1', ['treasury.view']), 'PUT', `${ORGANIZATION_API.roles}/r1/permissions`],
    ['save the special rights', (a) => a.setRoleSpecialRights('r1', {} as never), 'PUT', `${ORGANIZATION_API.roles}/r1/special-rights`],
    ['save the result rights', (a) => a.setRoleResultRights('r1', []), 'PUT', `${ORGANIZATION_API.roles}/r1/result-rights`],
    ['activate a role', (a) => a.setRoleActive('r1', true), 'POST', `${ORGANIZATION_API.roles}/r1/activate`],
    ['save a user', (a) => a.updateUser('u1', {} as never), 'PUT', `${ORGANIZATION_API.users}/u1`],
    ['assign a role', (a) => a.assignUserRole('u1', 'r1'), 'PUT', `${ORGANIZATION_API.users}/u1/role`],
    ['assign branches', (a) => a.assignUserBranches('u1', ['b1'], false), 'PUT', `${ORGANIZATION_API.users}/u1/branches`],
    ['save overrides', (a) => a.setUserOverrides('u1', {} as never), 'PUT', `${ORGANIZATION_API.users}/u1/overrides`],
    ['restore a user', (a) => a.restoreUser('u1'), 'POST', `${ORGANIZATION_API.users}/u1/restore`],
    ['lock a user', (a) => a.setUserLocked('u1', true), 'POST', `${ORGANIZATION_API.users}/u1/lock`],
    ['reset a password', (a) => a.resetUserPassword('u1', {} as never), 'POST', `${ORGANIZATION_API.users}/u1/reset-password`],
    ['deactivate a user', (a) => a.setUserActive('u1', false), 'POST', `${ORGANIZATION_API.users}/u1/deactivate`],
  ];

  for (const [name, call, method, url] of cases) {
    it(`${name}: a 204 resolves undefined, so mutation() reports success`, async () => {
      const write = mutation(() => call(api));
      const outcome = write.execute(undefined);
      backend.expectOne({ method, url }).flush(null, { status: 204, statusText: 'No Content' });

      expect(await outcome).toBeUndefined();
      expect(write.problem()).toBeNull();
    });
  }
});

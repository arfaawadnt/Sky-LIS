import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ORGANIZATION_PERMISSIONS } from '../../../core/api/organization.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { RolesFacade } from '../state/roles.facade';
import { UsersFacade } from '../state/users.facade';
import { BranchesPage } from './branches.page';
import { RolesPage } from './roles.page';
import { UsersPage } from './users.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/**
 * UAT R1-05: one export policy. The branches, roles and users registers offer "Export CSV" only to holders of
 * `organization.export`, as the catalogue and payers registers do with their module's export key; the view keys alone
 * (a reception role) no longer export.
 */
describe('Organization registers: Export CSV needs organization.export (UAT R1-05)', () => {
  let allowed: (key: string) => boolean = () => true;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        RolesFacade,
        UsersFacade,
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
  });

  async function exportOffered(page: Type<unknown>): Promise<boolean> {
    const fixture = TestBed.createComponent(page);
    TestBed.tick();
    await settle();
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('app-organization-export-button') !== null;
  }

  const pages: readonly (readonly [string, Type<unknown>])[] = [
    ['branches', BranchesPage],
    ['roles', RolesPage],
    ['users', UsersPage],
  ];

  for (const [name, page] of pages) {
    it(`${name}: hidden with every key but the export key, shown with it`, async () => {
      allowed = (key) => key !== ORGANIZATION_PERMISSIONS.export;
      expect(await exportOffered(page)).toBe(false);

      allowed = (key) => key === ORGANIZATION_PERMISSIONS.export || key === ORGANIZATION_PERMISSIONS.view || key === ORGANIZATION_PERMISSIONS.usersView;
      expect(await exportOffered(page)).toBe(true);
    });
  }
});

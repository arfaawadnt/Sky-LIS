import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AUTH_API, type AuthProfile, type AuthResponse } from '../core/api/auth.contracts';
import { ORGANIZATION_API, type SelectableBranch } from '../core/api/organization.contracts';
import { AuthStore } from '../core/auth/auth.store';
import { FakeBrowserNavigation, FakeSessionChannel, provideSessionTesting } from '../core/auth/session-testing';
import { PLATFORM_TENANT_ID } from '../core/tenancy/tenant-ids';
import { LabBrandComponent } from './lab-brand.component';
import { ShellComponent } from './shell.component';
import { ShellTopbarComponent } from './shell-topbar.component';

@Component({ selector: 'app-shell-lab-brand', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class LabBrandStub {}

@Component({ selector: 'app-page-stub', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class PageStub {}

const LAB = '11111111-1111-1111-1111-111111111111';
const MAIN: SelectableBranch = { id: 'b1', code: 'MAIN', nameEn: 'Main Branch', nameAr: 'الفرع الرئيسي' };
const NORTH: SelectableBranch = { id: 'b2', code: 'NORTH', nameEn: 'North Branch', nameAr: 'فرع الشمال' };

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function profile(tenantId: string, branchId: string | null): AuthProfile {
  return { userId: 'u1', displayName: 'Mona Adel', tenantId, roleName: 'Reception', permissions: [], language: 'en', scope: 'full', branchId, branchIds: [] };
}

/** Validation F-05 (CP-04): the user menu offers "Change branch" when the user has more than one branch; the top bar shows the branch. */
describe('ShellTopbarComponent: the session branch (F-05)', () => {
  let backend: HttpTestingController;
  let channel: FakeSessionChannel;

  beforeEach(async () => {
    channel = new FakeSessionChannel();
    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([{ path: '**', component: PageStub }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        ...provideSessionTesting(new FakeBrowserNavigation(), channel),
      ],
    })
      .overrideComponent(ShellTopbarComponent, { remove: { imports: [LabBrandComponent] }, add: { imports: [LabBrandStub] } })
      .compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function signIn(tenantId: string, branchId: string | null): Promise<void> {
    const pending = TestBed.inject(AuthStore).signIn('mona', 'pw', null);
    backend.expectOne(AUTH_API.signIn).flush({ step: 'signed-in', accessToken: 't', expiresAtUtc: null, refreshToken: null, profile: profile(tenantId, branchId), tenants: null } satisfies AuthResponse);
    await pending;
  }

  async function render(branches: readonly SelectableBranch[] | null): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<ShellComponent>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(ShellComponent);
    TestBed.tick();
    await settle();
    if (branches !== null) {
      backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: null, branches });
    }

    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  async function openMenu(fixture: ReturnType<typeof TestBed.createComponent<ShellComponent>>, host: HTMLElement): Promise<void> {
    host.querySelector<HTMLButtonElement>('.profile')?.click();
    await fixture.whenStable();
  }

  it('offers "Change branch" to a user with several branches and opens the selection in change mode, back to the page', async () => {
    await signIn(LAB, 'b1');
    await TestBed.inject(Router).navigateByUrl('/registration/new');
    const { fixture, host } = await render([MAIN, NORTH]);

    expect(host.querySelector('.branch-pill')?.textContent?.trim()).toBe('MAIN · Main Branch');
    await openMenu(fixture, host);
    expect(host.querySelector('.menu-branch')?.textContent?.trim()).toBe('MAIN · Main Branch');
    const change = host.querySelector<HTMLButtonElement>('[role="menu"] .mi.change-branch');
    expect(change?.textContent?.trim()).toBe('shell.changeBranch');

    change?.click();
    await fixture.whenStable();
    await settle();

    const router = TestBed.inject(Router);
    const url = router.parseUrl(router.url);
    expect(url.root.children['primary']?.segments.map((segment) => segment.path)).toEqual(['account', 'select-branch']);
    expect(url.queryParamMap.get('change')).toBe('1');
    expect(url.queryParamMap.get('returnUrl')).toBe('/registration/new');
    expect(host.querySelector('[role="menu"]')).toBeNull();
  });

  it('shows no "Change branch" when the user has one branch', async () => {
    await signIn(LAB, 'b1');
    const { fixture, host } = await render([MAIN]);

    await openMenu(fixture, host);
    expect(host.querySelector('[role="menu"]')).not.toBeNull();
    expect(host.querySelector('[role="menu"] .mi.change-branch')).toBeNull();
    expect(host.querySelector('.branch-pill')?.textContent?.trim()).toBe('MAIN · Main Branch');
  });

  it('follows a change of branch made in another tab: the top bar shows the new branch', async () => {
    await signIn(LAB, 'b1');
    const { fixture, host } = await render([MAIN, NORTH]);
    expect(host.querySelector('.branch-pill')?.textContent).toContain('MAIN');

    channel.deliver({ kind: 'branch-changed', userId: 'u1', tenantId: LAB, branchId: 'b2' });
    await fixture.whenStable();

    expect(host.querySelector('.branch-pill')?.textContent?.trim()).toBe('NORTH · North Branch');
  });

  it('reads no branches and shows no branch in the Super Admin portal', async () => {
    await signIn(PLATFORM_TENANT_ID, null);
    const { fixture, host } = await render(null);

    backend.expectNone(ORGANIZATION_API.selectableBranches);
    expect(host.querySelector('.branch-pill')).toBeNull();
    await openMenu(fixture, host);
    expect(host.querySelector('[role="menu"] .mi.change-branch')).toBeNull();
  });
});

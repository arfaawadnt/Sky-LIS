import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { ACCOUNT_API, type MyAccountProfile } from '../../../core/api/account.contracts';
import { AUTH_API, type AuthResponse } from '../../../core/api/auth.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PLATFORM_TENANT_ID } from '../../../core/tenancy/tenant-ids';
import { ProfileFacade } from '../state/profile.facade';
import { ProfilePage } from './profile.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function profile(mustChangePassword: boolean): MyAccountProfile {
  return {
    userId: 'u1',
    username: 'v14.user',
    displayName: 'Platform User',
    email: 'user@example.test',
    mobile: null,
    language: 'en',
    tenantId: PLATFORM_TENANT_ID,
    isPlatform: true,
    fullNameEn: null,
    fullNameAr: null,
    employeeCode: null,
    designation: null,
    roleNameEn: null,
    roleNameAr: null,
    mustChangePassword,
    passwordChangedAtUtc: '2026-10-11T08:00:00Z',
    passwordExpiresAtUtc: null,
    passwordPolicy: { minLength: 8, maxLength: 128 },
    laboratory: null,
  } as MyAccountProfile;
}

describe('ProfilePage — forced password change (C-07)', () => {
  let backend: HttpTestingController;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), ProfileFacade],
    });
    backend = TestBed.inject(HttpTestingController);
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  /** Signs in to the platform tenant (Super Admin portal) or a laboratory (client portal). */
  async function signIn(tenantId: string): Promise<void> {
    const signingIn = TestBed.inject(AuthStore).signIn('v14.user', 'pw', null);
    backend.expectOne(AUTH_API.signIn).flush({
      step: 'signed-in',
      accessToken: 't',
      expiresAtUtc: null,
      refreshToken: null,
      profile: { userId: 'u1', displayName: 'Platform User', tenantId, roleName: null, permissions: [], language: 'en', scope: 'full', branchId: null, branchIds: [] },
      tenants: null,
    } satisfies AuthResponse);
    await signingIn;
  }

  async function render(fixture: ComponentFixture<ProfilePage>): Promise<void> {
    fixture.detectChanges();
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  async function changePassword(fixture: ComponentFixture<ProfilePage>): Promise<void> {
    const root = fixture.nativeElement as HTMLElement;
    const type = (name: string, value: string): void => {
      const input = root.querySelector<HTMLInputElement>(`input[formcontrolname=${name}]`);
      if (!input) {
        throw new Error(`missing ${name}`);
      }

      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    type('current', 'temporary password');
    type('next', 'a brand new secret');
    type('confirmation', 'a brand new secret');
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('form button[type=submit]')?.click();
    await settle();
    backend.expectOne(ACCOUNT_API.password).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    await render(fixture);
  }

  async function forcedChange(tenantId: string): Promise<void> {
    await signIn(tenantId);
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.componentRef.setInput('mustChange', '1');
    await render(fixture);
    backend.expectOne(ACCOUNT_API.profile).flush(profile(true));
    await render(fixture);
    expect((fixture.nativeElement as HTMLElement).querySelector('.warn-strip')).not.toBeNull();

    await changePassword(fixture);
    backend.match(ACCOUNT_API.profile).forEach((request) => request.flush(profile(false)));
    await render(fixture);
  }

  it('B-04: a laboratory user leaves ?mustChange=1 for the client home (branch choice, then the landing page)', async () => {
    await forcedChange('11111111-1111-1111-1111-111111111111');

    expect(navigateByUrl).toHaveBeenCalledWith('/', { replaceUrl: true });
  });

  it('C-07: a platform user leaves ?mustChange=1 for the Super Admin home once the change succeeded in this tab', async () => {
    await forcedChange(PLATFORM_TENANT_ID);

    expect(navigateByUrl).toHaveBeenCalledWith('/admin', { replaceUrl: true });
  });

  it('stays on the profile after an ordinary password change', async () => {
    await signIn('11111111-1111-1111-1111-111111111111');
    const fixture = TestBed.createComponent(ProfilePage);
    await render(fixture);
    backend.expectOne(ACCOUNT_API.profile).flush(profile(false));
    await render(fixture);

    await changePassword(fixture);
    backend.match(ACCOUNT_API.profile).forEach((request) => request.flush(profile(false)));
    await render(fixture);

    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});

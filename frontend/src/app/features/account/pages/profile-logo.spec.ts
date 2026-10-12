import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ACCOUNT_API, type MyAccountProfile } from '../../../core/api/account.contracts';
import { AUTH_API, type AuthResponse } from '../../../core/api/auth.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { LaboratoryBrandService } from '../../../core/brand/laboratory-brand.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { ProfileFacade } from '../state/profile.facade';
import { ProfilePage } from './profile.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
const LAB = '11111111-1111-1111-1111-111111111111';

function profile(logoVersion: number | null): MyAccountProfile {
  return {
    userId: 'u1',
    username: 'lab.admin',
    displayName: 'Lab Admin',
    email: 'admin@example.test',
    mobile: null,
    language: 'en',
    tenantId: LAB,
    isPlatform: false,
    fullNameEn: null,
    fullNameAr: null,
    employeeCode: null,
    designation: null,
    roleNameEn: null,
    roleNameAr: null,
    mustChangePassword: false,
    passwordChangedAtUtc: '2026-10-11T08:00:00Z',
    passwordExpiresAtUtc: null,
    passwordPolicy: { minLength: 8, maxLength: 128 },
    laboratory: { tenantId: LAB, nameEn: 'Demo Laboratory', nameAr: null, portalUrl: null, countryNameEn: null, countryNameAr: null, planCode: null, planNameEn: null, planNameAr: null, managerName: null, managerMobile: null, mobile: null, email: null, status: null, trialEndsAtUtc: null, logoVersion },
  };
}

describe('ProfilePage — laboratory logo (read-only; it is changed on Setup › Parameters)', () => {
  let backend: HttpTestingController;
  let refreshed: number;

  beforeEach(() => {
    refreshed = 0;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        ProfileFacade,
        { provide: LaboratoryBrandService, useValue: { logoUrl: signal<string | null>(null), initials: signal('DL'), logoVersion: signal<number | null>(null), refresh: () => refreshed++ } },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function signIn(permissions: readonly string[]): Promise<void> {
    const signingIn = TestBed.inject(AuthStore).signIn('lab.admin', 'pw', null);
    backend.expectOne(AUTH_API.signIn).flush({
      step: 'signed-in',
      accessToken: 't',
      expiresAtUtc: null,
      refreshToken: null,
      profile: { userId: 'u1', displayName: 'Lab Admin', tenantId: LAB, roleName: null, permissions: [...permissions], language: 'en', scope: 'full', branchId: null, branchIds: [] },
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

  async function open(permissions: readonly string[], logoVersion: number | null): Promise<ComponentFixture<ProfilePage>> {
    await signIn(permissions);
    const fixture = TestBed.createComponent(ProfilePage);
    await render(fixture);
    backend.expectOne(ACCOUNT_API.profile).flush(profile(logoVersion));
    await render(fixture);
    return fixture;
  }

  it('shows the laboratory logo read-only and sends the manager to Setup › Parameters', async () => {
    const fixture = await open(['settings.manage'], 2);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.logo-badge')?.textContent?.trim()).toBe('DL');
    expect(root.querySelector('input[type=file]')).toBeNull();   // no upload here any more
    const link = root.querySelector<HTMLAnchorElement>('.logo-text a');
    expect(link?.getAttribute('href')).toBe('/organization/parameters');
    expect(link?.textContent).toContain('account.profile.logoManage');
    expect(refreshed).toBe(0);
    backend.expectNone(ACCOUNT_API.laboratoryLogo);
  });

  it('offers no link to a user who cannot change the logo', async () => {
    const fixture = await open([], 2);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.logo-row')).not.toBeNull();
    expect(root.querySelector('.logo-text a')).toBeNull();
  });
});

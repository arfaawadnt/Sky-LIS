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
import { ToastService } from '../../../shared/ui/toast/toast.service';
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

describe('ProfilePage — laboratory logo', () => {
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

  function choose(fixture: ComponentFixture<ProfilePage>, file: File): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('.logo-actions input[type=file]');
    if (!input) {
      throw new Error('no file input');
    }

    Object.defineProperty(input, 'files', { configurable: true, value: [file] });
    input.dispatchEvent(new Event('change'));
  }

  it('uploads the chosen image and refreshes the page and the top bar', async () => {
    const fixture = await open(['settings.manage'], null);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.logo-badge')?.textContent?.trim()).toBe('DL');
    expect(root.querySelector('.logo-actions label')?.textContent).toContain('account.profile.logoUpload');
    expect(root.querySelector('.logo-actions .btn.ghost')).toBeNull();   // nothing to remove yet

    choose(fixture, new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'logo.png', { type: 'image/png' }));
    await settle();
    const upload = backend.expectOne({ method: 'POST', url: ACCOUNT_API.laboratoryLogo });
    expect((upload.request.body as FormData).get('file')).toBeInstanceOf(File);
    upload.flush({ version: 1, contentType: 'image/png', sizeBytes: 4 });
    await settle();
    await render(fixture);   // the facade re-reads on the next tick

    backend.expectOne(ACCOUNT_API.profile).flush(profile(1));
    await render(fixture);
    expect(refreshed).toBe(1);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
    expect(root.querySelector('.logo-actions label')?.textContent).toContain('account.profile.logoReplace');
    expect(root.querySelector('.logo-actions .btn.ghost')).not.toBeNull();
  });

  it('shows the refusal of an image the server does not accept', async () => {
    const fixture = await open(['settings.manage'], null);
    choose(fixture, new File(['<svg/>'], 'logo.svg', { type: 'image/svg+xml' }));
    await settle();
    backend.expectOne({ method: 'POST', url: ACCOUNT_API.laboratoryLogo }).flush(
      { type: 'about:blank', title: 'Refused', status: 422, code: 'ACC-042', detail: 'The logo must be a PNG, JPEG or WebP image.' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await settle();

    expect(refreshed).toBe(0);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'error')).toBe(true);
    backend.expectNone(ACCOUNT_API.profile);
  });

  it('removes the logo', async () => {
    const fixture = await open(['settings.manage'], 3);
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.logo-actions .btn.ghost')?.click();
    await settle();
    backend.expectOne({ method: 'DELETE', url: ACCOUNT_API.laboratoryLogo }).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    await render(fixture);
    backend.expectOne(ACCOUNT_API.profile).flush(profile(null));
    await render(fixture);
    expect(refreshed).toBe(1);
  });

  it('offers no upload to a user without settings.manage', async () => {
    const fixture = await open([], 2);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.logo-row')).not.toBeNull();
    expect(root.querySelector('.logo-actions')).toBeNull();
  });
});

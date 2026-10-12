import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ACCOUNT_API } from '../core/api/account.contracts';
import { AuthStore } from '../core/auth/auth.store';
import { LaboratoryBrandService, labInitials } from '../core/brand/laboratory-brand.service';
import { I18nService } from '../core/i18n/i18n.service';
import { PortalService } from '../core/portal/portal.service';
import { LabBrandComponent } from './lab-brand.component';

const tenant = signal<string | null>('t-1');
const portal = signal<'client' | 'admin'>('client');

function profile(laboratory: { nameEn: string | null; nameAr: string | null; logoVersion?: number | null } | null): object {
  return { userId: 'u1', username: 'mona', displayName: 'Mona', tenantId: tenant(), isPlatform: laboratory === null, laboratory: laboratory === null ? null : { tenantId: 't-1', ...laboratory } };
}

async function settle(): Promise<void> {
  for (let round = 0; round < 3; round++) {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
}

describe('LabBrandComponent (the client laboratory in the top bar)', () => {
  const created: string[] = [];
  const revoked: string[] = [];

  beforeAll(() => {
    // jsdom has no object URLs
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: () => { created.push(`blob:logo-${created.length + 1}`); return created[created.length - 1]; } });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: (url: string) => revoked.push(url) });
  });

  beforeEach(() => {
    tenant.set('t-1');
    portal.set('client');
    created.length = 0;
    revoked.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { isAuthenticated: computed(() => tenant() !== null), currentTenant: tenant } },
        { provide: PortalService, useValue: { portal, homePath: computed(() => (portal() === 'admin' ? '/admin' : '/')) } },
      ],
    });
  });

  it('shows the laboratory name in the language of the page with its initials while it has no logo', async () => {
    const fixture = TestBed.createComponent(LabBrandComponent);
    await settle();
    const backend = TestBed.inject(HttpTestingController);
    backend.expectOne(ACCOUNT_API.profile).flush(profile({ nameEn: 'Demo Laboratory', nameAr: 'معمل العرض', logoVersion: null }));
    await settle();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.name')?.textContent?.trim()).toBe('Demo Laboratory');
    expect(host.querySelector('.badge')?.textContent?.trim()).toBe('DL');
    expect(host.querySelector('img')).toBeNull();
    expect(host.querySelector('a')?.getAttribute('href')).toBe('/');
    backend.expectNone(ACCOUNT_API.laboratoryLogo);

    TestBed.inject(I18nService).setLanguage('ar');
    await settle();
    expect(host.querySelector('.name')?.textContent?.trim()).toBe('معمل العرض');
    expect(host.querySelector('.badge')?.textContent?.trim()).toBe('مع');
  });

  it('shows the logo when the laboratory has one, and a new version after a refresh', async () => {
    const fixture = TestBed.createComponent(LabBrandComponent);
    await settle();
    const backend = TestBed.inject(HttpTestingController);
    backend.expectOne(ACCOUNT_API.profile).flush(profile({ nameEn: 'Demo Laboratory', nameAr: null, logoVersion: 1 }));
    await settle();
    backend.expectOne(ACCOUNT_API.laboratoryLogo).flush(new Blob(['png'], { type: 'image/png' }));
    await settle();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('img.logo')?.getAttribute('src')).toBe('blob:logo-1');
    expect(host.querySelector('.badge')).toBeNull();

    // the profile page refreshes after an upload: the new version is fetched and the old URL revoked
    TestBed.inject(LaboratoryBrandService).refresh();
    await settle();
    backend.expectOne(ACCOUNT_API.profile).flush(profile({ nameEn: 'Demo Laboratory', nameAr: null, logoVersion: 2 }));
    await settle();
    backend.expectOne(ACCOUNT_API.laboratoryLogo).flush(new Blob(['png2'], { type: 'image/png' }));
    await settle();
    expect(host.querySelector('img.logo')?.getAttribute('src')).toBe('blob:logo-2');
    expect(revoked).toContain('blob:logo-1');
  });

  it('shows the logo alone for a laboratory without a client record, and nothing without a logo either', async () => {
    const fixture = TestBed.createComponent(LabBrandComponent);
    await settle();
    const backend = TestBed.inject(HttpTestingController);
    backend.expectOne(ACCOUNT_API.profile).flush(profile({ nameEn: null, nameAr: null, logoVersion: 4 }));
    await settle();
    backend.expectOne(ACCOUNT_API.laboratoryLogo).flush(new Blob(['png'], { type: 'image/png' }));
    await settle();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('img.logo')).not.toBeNull();
    expect(host.querySelector('.name')).toBeNull();

    TestBed.inject(LaboratoryBrandService).refresh();
    await settle();
    backend.expectOne(ACCOUNT_API.profile).flush(profile({ nameEn: null, nameAr: null, logoVersion: null }));
    await settle();
    expect(host.querySelector('a')).toBeNull();
  });

  it('falls back to the other language when one name is missing', async () => {
    const fixture = TestBed.createComponent(LabBrandComponent);
    TestBed.inject(I18nService).setLanguage('ar');
    await settle();
    TestBed.inject(HttpTestingController).expectOne(ACCOUNT_API.profile).flush(profile({ nameEn: 'City Lab', nameAr: null }));
    await settle();

    expect((fixture.nativeElement as HTMLElement).querySelector('.name')?.textContent?.trim()).toBe('City Lab');
  });

  it('shows nothing in the Super Admin portal and asks nothing', async () => {
    portal.set('admin');
    const fixture = TestBed.createComponent(LabBrandComponent);
    await settle();

    TestBed.inject(HttpTestingController).expectNone(ACCOUNT_API.profile);
    expect((fixture.nativeElement as HTMLElement).querySelector('a')).toBeNull();
  });

  it('makes up to two initials, skipping the Arabic article', () => {
    expect(labInitials('Demo Laboratory')).toBe('DL');
    expect(labInitials('  al shifa  medical lab ')).toBe('AS');
    expect(labInitials('Cairo')).toBe('C');
    expect(labInitials('المختبر الحديث')).toBe('مح');
    expect(labInitials('ال')).toBe('ا');
    expect(labInitials('')).toBe('');
  });
});

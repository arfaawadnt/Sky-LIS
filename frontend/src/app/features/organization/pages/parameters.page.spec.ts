import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import type { TenantSettingItem } from '../../../core/api/settings.contracts';
import { LaboratoryBrandService } from '../../../core/brand/laboratory-brand.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ParametersFacade } from '../state/parameters.facade';
import { ParametersPage, isValidParameter } from './parameters.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function item(key: string, type: TenantSettingItem['type'], value: string, defaultValue: string): TenantSettingItem {
  return { key, type, value, defaultValue, description: key, isDefault: value === defaultValue, updatedAtUtc: null, updatedByUserId: null };
}

describe('ParametersPage (CP-57)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ParametersPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ParametersFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: LaboratoryBrandService, useValue: { logoUrl: signal<string | null>(null), initials: signal('DL'), logoVersion: signal<number | null>(null) } },
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

  it('shows a laboratory the four Release 1 groups with its logo, locks auto-collect and auto-receive, hides Subscription', async () => {
    const fixture = TestBed.createComponent(ParametersPage);
    await flush();
    backend.expectOne(ORGANIZATION_API.settings).flush({
      settings: [
        item('mandatoryMobile', 'Bool', 'false', 'false'),
        item('labelCopies', 'Int', '1', '1'),
        item('autoCollect', 'Bool', 'true', 'true'),
        item('autoReceive', 'Bool', 'true', 'true'),
        item('reviewBeforeVerify', 'Bool', 'false', 'false'),
        item('sessionTimeoutMinutes', 'Int', '30', '30'),
        item('masterDataUpdatePolicy', 'String', 'propose', 'propose'),
      ],
    });
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('app-laboratory-logo-card')).not.toBeNull();
    expect(host.querySelectorAll('form app-section-card').length).toBe(4);   // registration, printing, samples, security
    expect(host.querySelector('#parameter-masterDataUpdatePolicy')).toBeNull();   // Subscription is Release 2
    expect(host.querySelector<HTMLInputElement>('#parameter-autoCollect')?.disabled).toBe(true);
    expect(host.querySelector<HTMLInputElement>('#parameter-autoReceive')?.disabled).toBe(true);
    expect(host.querySelector<HTMLInputElement>('#parameter-reviewBeforeVerify')?.disabled).toBe(false);
    expect(host.querySelectorAll('.locked').length).toBe(2);
    expect([...host.querySelectorAll('button')].some((button) => button.textContent?.includes('organization.save'))).toBe(true);
  });

  it('groups the parameters with typed inputs and saves the changed value (Release 2)', async () => {
    const fixture = TestBed.createComponent(ParametersPage);
    (fixture.componentInstance as unknown as { scope: WritableSignal<'R1' | 'R2'> }).scope.set('R2');
    await flush();
    backend.expectOne(ORGANIZATION_API.settings).flush({
      settings: [item('mandatoryMobile', 'Bool', 'false', 'false'), item('labelCopies', 'Int', '1', '1'), item('resultLanguage', 'String', 'en', 'en'), item('trialDays', 'Int', '14', '14')],
    });
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('form app-section-card').length).toBe(2);   // registration, printing; trialDays is platform-only
    expect(host.querySelector('app-laboratory-logo-card')).not.toBeNull();   // a laboratory keeps its logo card
    expect(host.querySelector('#parameter-trialDays')).toBeNull();
    const toggle = host.querySelector<HTMLInputElement>('#parameter-mandatoryMobile');
    expect(toggle?.type).toBe('checkbox');
    expect(host.querySelector<HTMLSelectElement>('#parameter-resultLanguage')?.tagName).toBe('SELECT');

    toggle?.click();
    await flush();
    const save = [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('organization.save'));
    save?.click();
    await flush();

    const put = backend.expectOne({ method: 'PUT', url: ORGANIZATION_API.settings });
    expect(put.request.body).toEqual({ settings: [{ key: 'mandatoryMobile', value: 'true' }] });
    put.flush({ settings: [item('mandatoryMobile', 'Bool', 'true', 'false')] });
    backend.verify();
  });

  it('validates values against their descriptor before sending', () => {
    expect(isValidParameter({ key: 'labelCopies', input: 'number', min: 1, max: 10 }, '11')).toBe(false);
    expect(isValidParameter({ key: 'labelCopies', input: 'number', min: 1, max: 10 }, '3')).toBe(true);
    expect(isValidParameter({ key: 'labelCopies', input: 'number', min: 1, max: 10 }, '2.5')).toBe(false);
    expect(isValidParameter({ key: 'resultLanguage', input: 'select', options: ['en', 'ar'] }, 'fr')).toBe(false);
    expect(isValidParameter({ key: 'accessionFormat', input: 'text', maxLength: 200 }, ' ')).toBe(false);
    expect(isValidParameter({ key: 'autoCollect', input: 'switch' }, 'true')).toBe(true);
  });
});

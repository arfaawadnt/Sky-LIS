import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import type { TenantSettingItem } from '../../../core/api/settings.contracts';
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

  it('groups the parameters with typed inputs and saves the changed value', async () => {
    const fixture = TestBed.createComponent(ParametersPage);
    await flush();
    backend.expectOne(ORGANIZATION_API.settings).flush({
      settings: [item('mandatoryMobile', 'Bool', 'false', 'false'), item('labelCopies', 'Int', '1', '1'), item('resultLanguage', 'String', 'en', 'en'), item('trialDays', 'Int', '14', '14')],
    });
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('app-section-card').length).toBe(2);   // registration, printing; trialDays is platform-only
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

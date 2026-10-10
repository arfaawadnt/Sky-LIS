import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import type { TenantSettingItem, TenantSettingsResponse } from '../../../core/api/settings.contracts';
import { ParametersFacade } from './parameters.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function item(key: string, type: TenantSettingItem['type'], value: string, defaultValue: string): TenantSettingItem {
  return { key, type, value, defaultValue, description: key, isDefault: value === defaultValue, updatedAtUtc: null, updatedByUserId: null };
}

const stored: TenantSettingsResponse = {
  settings: [item('autoCollect', 'Bool', 'true', 'true'), item('labelCopies', 'Int', '2', '1'), item('resultLanguage', 'String', 'en', 'en')],
};

describe('ParametersFacade (CP-57)', () => {
  let facade: ParametersFacade;
  let backend: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), ParametersFacade] });
    facade = TestBed.inject(ParametersFacade);
    backend = TestBed.inject(HttpTestingController);
    TestBed.tick();
    await settle();
    backend.expectOne(ORGANIZATION_API.settings).flush(stored);
    TestBed.tick();
    await settle();
  });

  it('tracks only real changes and returns a value to its default', () => {
    expect(facade.valueOf('labelCopies')).toBe('2');

    facade.set('autoCollect', 'false');
    facade.set('autoCollect', 'true');   // back to the stored value: no change
    facade.resetToDefault('labelCopies');

    expect(facade.changes()).toEqual([{ key: 'labelCopies', value: '1' }]);
    expect(facade.dirty()).toBe(true);

    facade.discard();
    expect(facade.dirty()).toBe(false);
  });

  it('saves only the changed keys and adopts the effective list the server returns', async () => {
    facade.set('resultLanguage', 'ar');

    const saving = facade.save.execute(facade.changes());
    await settle();
    const put = backend.expectOne({ method: 'PUT', url: ORGANIZATION_API.settings });
    expect(put.request.body).toEqual({ settings: [{ key: 'resultLanguage', value: 'ar' }] });
    put.flush({ settings: [item('autoCollect', 'Bool', 'true', 'true'), item('labelCopies', 'Int', '2', '1'), item('resultLanguage', 'String', 'ar', 'en')] });
    await saving;

    expect(facade.dirty()).toBe(false);
    expect(facade.valueOf('resultLanguage')).toBe('ar');
    backend.verify();
  });
});

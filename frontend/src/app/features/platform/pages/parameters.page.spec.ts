import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { SETTINGS_API, type TenantSettingItem, type TenantSettingsResponse } from '../../../core/api/settings.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ParametersFacade } from '../state/reference.facades';
import { ParametersPage } from './parameters.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function item(key: string, type: TenantSettingItem['type'], value: string, defaultValue = value): TenantSettingItem {
  return { key, type, value, defaultValue, description: `${key} description`, isDefault: value === defaultValue, updatedAtUtc: null, updatedByUserId: null };
}

const response: TenantSettingsResponse = {
  settings: [
    item('otpLength', 'Int', '6'),
    item('passwordMinLength', 'Int', '8'),
    item('trialDays', 'Int', '14'),
    item('masterDataUpdatePolicy', 'String', 'propose'),
    item('autoCollect', 'Bool', 'true'),
  ],
};

describe('ParametersPage (SA-19)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ParametersPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        ParametersFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<ParametersPage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(ParametersPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === SETTINGS_API.settings).flush(response);
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  it('renders typed controls: numbers, a closed set and a checkbox', async () => {
    const { host } = await render();

    expect(host.querySelectorAll('input[type="number"]').length).toBe(3);
    const policy = host.querySelector<HTMLSelectElement>('select');
    expect(Array.from(policy?.options ?? []).map((option) => option.value)).toEqual(['push', 'propose', 'ignore']);
    expect(host.querySelector<HTMLInputElement>('input[type="checkbox"]')?.checked).toBe(true);
  });

  it('saves only the changed keys in their text form', async () => {
    const { fixture, host } = await render();

    const trial = Array.from(host.querySelectorAll<HTMLInputElement>('input[type="number"]'))[2];
    if (!trial) {
      throw new Error('trialDays control not found');
    }

    trial.value = '30';
    trial.dispatchEvent(new Event('input', { bubbles: true }));
    const checkbox = host.querySelector<HTMLInputElement>('input[type="checkbox"]');
    checkbox?.click();
    await fixture.whenStable();

    host.querySelector<HTMLFormElement>('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await settle();

    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === SETTINGS_API.settings);
    expect(put.request.body).toEqual({ settings: [{ key: 'trialDays', value: '30' }, { key: 'autoCollect', value: 'false' }] });
    put.flush(response);
    await settle();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === SETTINGS_API.settings).flush(response);
    backend.verify();
  });

  describe('the time zone (round 3 R3-03)', () => {
    const withZone: TenantSettingsResponse = { settings: [...response.settings, item('timeZone', 'String', 'Africa/Cairo')] };

    async function renderZone(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<ParametersPage>>; host: HTMLElement; zone: HTMLInputElement }> {
      const fixture = TestBed.createComponent(ParametersPage);
      TestBed.tick();
      await settle();
      backend.expectOne((request) => request.method === 'GET' && request.url === SETTINGS_API.settings).flush(withZone);
      await settle();
      await fixture.whenStable();
      const host = fixture.nativeElement as HTMLElement;
      const zone = Array.from(host.querySelectorAll<HTMLInputElement>('input[type="text"]')).find((input) => input.value === 'Africa/Cairo');
      if (!zone) {
        throw new Error('timeZone control not found');
      }

      return { fixture, host, zone };
    }

    function submit(host: HTMLElement): void {
      host.querySelector<HTMLFormElement>('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    }

    it('limits the field to the 64 characters the server accepts', async () => {
      const { zone } = await renderZone();
      expect(zone.getAttribute('maxlength')).toBe('64');
    });

    it('says why a save is blocked by a malformed value instead of doing nothing', async () => {
      const { fixture, host, zone } = await renderZone();
      const warn = vi.spyOn(TestBed.inject(ToastService), 'warn');
      zone.value = 'not a zone!';
      zone.dispatchEvent(new Event('input', { bubbles: true }));
      await fixture.whenStable();

      submit(host);
      await settle();
      await fixture.whenStable();

      backend.expectNone((request) => request.method === 'PUT');
      expect(warn).toHaveBeenCalledWith(expect.stringContaining('masterData.invalid'));
      fixture.detectChanges();
      TestBed.tick();
      await settle();
      expect(zone.getAttribute('aria-invalid')).toBe('true');
    });

    it('shows a refused zone under its field, not in the page banner', async () => {
      const { fixture, host, zone } = await renderZone();
      zone.value = 'Mars/Olympus';
      zone.dispatchEvent(new Event('input', { bubbles: true }));
      await fixture.whenStable();

      submit(host);
      await settle();
      backend
        .expectOne((request) => request.method === 'PUT' && request.url === SETTINGS_API.settings)
        .flush({ code: 'SET-002', title: 'Invalid value', status: 422, detail: "'Mars/Olympus' is not a valid String value for 'timeZone'." }, { status: 422, statusText: 'Unprocessable Entity' });
      await settle();
      await fixture.whenStable();

      fixture.detectChanges();
      TestBed.tick();
      await settle();
      expect(zone.getAttribute('aria-invalid')).toBe('true');
      expect(host.querySelector('.banner, app-alert, [role="alert"].page-problem')).toBeNull();
    });
  });
});

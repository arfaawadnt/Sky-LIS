import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import platformAr from '../../../../../public/i18n/platform.ar.json';
import platformEn from '../../../../../public/i18n/platform.en.json';
import { I18nService } from '../../../core/i18n/i18n.service';
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

  it('validation F-13: "Use default" removes the platform value instead of storing the default, and says "1 parameter saved."', async () => {
    const stored: TenantSettingsResponse = {
      settings: [
        { ...item('otpLength', 'Int', '8', '6'), isDefault: false, updatedAtUtc: '2026-10-12T10:00:00Z' },
        { ...item('passwordMinLength', 'Int', '8'), isDefault: false, updatedAtUtc: '2026-10-12T10:00:00Z' },   // a row equal to the default
        item('trialDays', 'Int', '14'),
      ],
    };
    vi.stubGlobal('fetch', (url: string) => Promise.resolve(new Response(JSON.stringify(url.includes('/platform.en.json') ? platformEn : {}), { status: 200 })));
    try {
      const success = vi.spyOn(TestBed.inject(ToastService), 'success');
      const fixture = TestBed.createComponent(ParametersPage);
      TestBed.tick();
      await settle();
      backend.expectOne((request) => request.method === 'GET' && request.url === SETTINGS_API.settings).flush(stored);
      await settle();
      await fixture.whenStable();
      const host = fixture.nativeElement as HTMLElement;
      const useDefaultButtons = (): HTMLButtonElement[] => [...host.querySelectorAll<HTMLButtonElement>('button')].filter((button) => button.textContent?.trim() === platformEn['parameters.useDefault']);

      expect(useDefaultButtons().length).toBe(2);   // otpLength (modified) and passwordMinLength (a row of its own); not trialDays
      useDefaultButtons()[1]?.click();   // passwordMinLength
      await fixture.whenStable();
      expect(useDefaultButtons().length).toBe(1);

      host.querySelector<HTMLFormElement>('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      await settle();
      const put = backend.expectOne((request) => request.method === 'PUT' && request.url === SETTINGS_API.settings);
      expect(put.request.body).toEqual({ settings: [{ key: 'passwordMinLength', value: null }] });
      put.flush({ settings: [stored.settings[0], item('passwordMinLength', 'Int', '8'), item('trialDays', 'Int', '14')] });
      await settle();
      expect(success).toHaveBeenCalledWith('1 parameter saved.');
      TestBed.tick();
      await settle();
      backend.expectOne((request) => request.method === 'GET' && request.url === SETTINGS_API.settings).flush(stored);
    } finally {
      vi.unstubAllGlobals();
    }
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

    it('LIS-VR-002 round 4: an unknown zone (SET-003) reads "This time zone is not known." under the field, also when other values were sent', async () => {
      // the page's dictionary, served as the app serves it
      vi.stubGlobal('fetch', (url: string) => Promise.resolve(new Response(JSON.stringify(url.includes('/platform.en.json') ? platformEn : {}), { status: 200 })));
      try {
        const { fixture, host, zone } = await renderZone();
        zone.value = 'Mars/Olympus';
        zone.dispatchEvent(new Event('input', { bubbles: true }));
        const trial = Array.from(host.querySelectorAll<HTMLInputElement>('input[type="number"]'))[2];
        if (trial) {
          trial.value = '30';
          trial.dispatchEvent(new Event('input', { bubbles: true }));
        }
        await fixture.whenStable();

        submit(host);
        await settle();
        const put = backend.expectOne((request) => request.method === 'PUT' && request.url === SETTINGS_API.settings);
        expect((put.request.body as { settings: unknown[] }).settings.length).toBe(2);
        put.flush({ code: 'SET-003', title: 'Unknown time zone', status: 422, detail: "'Mars/Olympus' is not a time zone this host knows for 'timeZone'." }, { status: 422, statusText: 'Unprocessable Entity' });
        await settle();
        await fixture.whenStable();
        fixture.detectChanges();
        TestBed.tick();
        await settle();

        expect(zone.getAttribute('aria-invalid')).toBe('true');
        expect(zone.closest('app-form-field')?.textContent).toContain('This time zone is not known.');
        expect(host.textContent).not.toContain('One of the values is outside its allowed range.');
        expect(TestBed.inject(I18nService).t('platform.parameters.error.SET-003')).toBe('This time zone is not known.');
      } finally {
        vi.unstubAllGlobals();
      }
    });

    it('LIS-VR-002 round 4: the unknown-zone text exists in both languages', () => {
      const en: Record<string, string> = platformEn;
      const ar: Record<string, string> = platformAr;
      expect(en['parameters.error.SET-003']).toBe('This time zone is not known.');
      expect(ar['parameters.error.SET-003']).toBe('هذه المنطقة الزمنية غير معروفة.');
    });
  });
});

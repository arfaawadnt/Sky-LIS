import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PUBLIC_SUBSCRIPTION_API, type PublicCountry } from '../../../core/api/public-subscription.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { SubscriptionFacade } from '../state/subscription.facade';
import { LandingPage } from './landing.page';
import { SubscribePage } from './subscribe.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const EGYPT: PublicCountry = { id: 'eg', nameEn: 'Egypt', nameAr: 'مصر', isoCode: 'EG', dialCode: '+20', mobileLength: 10, flag: null, currencyCode: 'EGP', language: 'ar' };

function setValue(root: HTMLElement, selector: string, value: string): void {
  const element = root.querySelector<HTMLInputElement | HTMLSelectElement>(selector);
  if (!element) {
    throw new Error(`missing ${selector}`);
  }

  element.value = value;
  element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input'));
}

describe('Public pages (CP-01, CP-02)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), SubscriptionFacade],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  it('the landing page offers sign-in and subscription', async () => {
    const fixture = TestBed.createComponent(LandingPage);
    await fixture.whenStable();
    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('a')].map((anchor) => anchor.getAttribute('href'));
    expect(links).toContain('/welcome/subscribe');
    expect(links).toContain('/login');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('app-stat-card').length).toBe(4);
  });

  describe('the wizard', () => {
    let fixture: ComponentFixture<SubscribePage>;
    let root: HTMLElement;

    async function render(): Promise<void> {
      fixture.detectChanges();
      TestBed.tick();
      await settle();
      fixture.detectChanges();
    }

    beforeEach(async () => {
      fixture = TestBed.createComponent(SubscribePage);
      root = fixture.nativeElement as HTMLElement;
      await render();
      backend.expectOne(PUBLIC_SUBSCRIPTION_API.countries).flush([EGYPT]);
      await render();
    });

    it('renders a shown digit-count message again when the language changes (re-test R-02)', async () => {
      const i18n = TestBed.inject(I18nService);
      const tf = i18n.tf.bind(i18n);
      vi.spyOn(i18n, 'tf').mockImplementation((key: string, params: Readonly<Record<string, string | number>>) =>
        key === 'public.wizard.validation.mobileLength' || key === 'public.wizard.validation.managerMobileLength'
          ? `${i18n.lang()}:${key.endsWith('managerMobileLength') ? 'manager' : 'lab'}:${String(params['length'])}`
          : tf(key, params),
      );
      setValue(root, 'select[formcontrolname=countryId]', 'eg');
      await render();
      backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.governorates).flush([]);
      for (const name of ['mobile', 'managerMobile']) {
        setValue(root, `input[formcontrolname=${name}]`, '12345');
        root.querySelector(`input[formcontrolname=${name}]`)?.dispatchEvent(new Event('blur'));
      }
      await render();
      const shown = (): string => root.textContent ?? '';
      expect(shown()).toContain('en:lab:10');
      expect(shown()).toContain('en:manager:10');

      // Round 2 RA-02: the text on screen changes, not only the validator's result.
      i18n.setLanguage('ar');
      await render();
      expect(shown()).toContain('ar:lab:10');
      expect(shown()).toContain('ar:manager:10');
      expect(shown()).not.toContain('en:lab:10');
      expect(shown()).not.toContain('en:manager:10');

      i18n.setLanguage('en');
      await render();
      expect(shown()).toContain('en:lab:10');
      expect(shown()).not.toContain('ar:manager:10');
    });

    it('does not send the code while the details are incomplete or a mobile has the wrong length', async () => {
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      backend.expectNone(PUBLIC_SUBSCRIPTION_API.start);
      expect(root.querySelectorAll('[aria-invalid=true]').length).toBeGreaterThan(0);

      setValue(root, 'select[formcontrolname=countryId]', 'eg');
      await render();
      backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.governorates).flush([{ id: 'cairo', countryId: 'eg', nameEn: 'Cairo', nameAr: 'القاهرة', district: null }]);
      await render();
      setValue(root, 'input[formcontrolname=labNameEn]', 'Al Noor Laboratories');
      setValue(root, 'input[formcontrolname=labNameAr]', 'مختبرات النور');
      setValue(root, 'select[formcontrolname=governorateId]', 'cairo');
      setValue(root, 'input[formcontrolname=address]', 'Nasr City');
      setValue(root, 'input[formcontrolname=mobile]', '0101234567');
      setValue(root, 'input[formcontrolname=managerName]', 'Ahmed Hassan');
      setValue(root, 'input[formcontrolname=managerMobile]', '01012345679');
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      backend.expectNone(PUBLIC_SUBSCRIPTION_API.start);
      // C-08: the wrong mobile says the country's digit count (as Add client does), not the generic "The value is not valid."
      expect(root.textContent).toContain('public.wizard.validation.mobileLength');
      expect(root.textContent).not.toContain('public.wizard.validation.managerMobileLength');
      expect(root.textContent).not.toContain('shared.validation.invalid');

      setValue(root, 'input[formcontrolname=mobile]', '01012345678');
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      const start = backend.expectOne(PUBLIC_SUBSCRIPTION_API.start);
      expect(start.request.body).toMatchObject({ labNameEn: 'Al Noor Laboratories', countryId: 'eg', governorateId: 'cairo', mobile: '01012345678', licenceNo: null, email: null, language: 'en' });
      start.flush({ subscriptionId: 'sub-1', expiresAtUtc: '2026-10-09T13:00:00Z', maskedMobile: '+201******679', codeLifetimeSeconds: 300 });
      await render();
      expect(root.textContent).toContain('public.wizard.verifyTitle');
      expect(root.querySelector<HTMLButtonElement>('button.link')?.disabled).toBe(true);
      // without a revealed code (an SMS gateway sends it) the page shows nothing but the masked mobile
      expect(root.querySelector('.code-reveal')).toBeNull();
    });

    it('shows the verification code on the page when the host reveals it (no SMS gateway yet, 2026-10-14)', async () => {
      setValue(root, 'select[formcontrolname=countryId]', 'eg');
      await render();
      backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.governorates).flush([{ id: 'cairo', countryId: 'eg', nameEn: 'Cairo', nameAr: 'القاهرة' }]);
      await render();
      setValue(root, 'input[formcontrolname=labNameEn]', 'Al Noor Laboratories');
      setValue(root, 'input[formcontrolname=labNameAr]', 'مختبرات النور');
      setValue(root, 'select[formcontrolname=governorateId]', 'cairo');
      setValue(root, 'input[formcontrolname=address]', 'Nasr City');
      setValue(root, 'input[formcontrolname=mobile]', '01012345678');
      setValue(root, 'input[formcontrolname=managerName]', 'Ahmed Hassan');
      setValue(root, 'input[formcontrolname=managerMobile]', '01012345679');
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      backend.expectOne(PUBLIC_SUBSCRIPTION_API.start).flush({ subscriptionId: 'sub-1', expiresAtUtc: '2026-10-09T13:00:00Z', maskedMobile: '+201******679', codeLifetimeSeconds: 300, verificationCode: '482913' });
      await render();
      const reveal = root.querySelector('.code-reveal');
      expect(reveal?.textContent).toContain('public.wizard.codeShown');
      expect(reveal?.querySelector('.mono')?.textContent?.trim()).toBe('482913');
      // the code is shown, not typed in: the person still enters it
      expect(root.querySelector<HTMLInputElement>('input[formcontrolname=code]')?.value).toBe('');
    });
  });
});

import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

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
        setValue(root, `app-mobile-input[formcontrolname=${name}] input.number`, '12345');
        root.querySelector(`app-mobile-input[formcontrolname=${name}] input.number`)?.dispatchEvent(new Event('blur'));
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
      setValue(root, 'app-mobile-input[formcontrolname=mobile] input.number', '0101234567');
      setValue(root, 'input[formcontrolname=managerName]', 'Ahmed Hassan');
      setValue(root, 'app-mobile-input[formcontrolname=managerMobile] input.number', '01012345679');
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      backend.expectNone(PUBLIC_SUBSCRIPTION_API.start);
      // C-08: the wrong mobile says the country's digit count (as Add client does), not the generic "The value is not valid."
      expect(root.textContent).toContain('public.wizard.validation.mobileLength');
      expect(root.textContent).not.toContain('public.wizard.validation.managerMobileLength');
      expect(root.textContent).not.toContain('shared.validation.invalid');

      setValue(root, 'app-mobile-input[formcontrolname=mobile] input.number', '01012345678');
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      const start = backend.expectOne(PUBLIC_SUBSCRIPTION_API.start);
      expect(start.request.body).toMatchObject({ labNameEn: 'Al Noor Laboratories', countryId: 'eg', governorateId: 'cairo', mobile: '+201012345678', managerMobile: '+201012345679', licenceNo: null, email: null, language: 'en' });
      start.flush({ subscriptionId: 'sub-1', expiresAtUtc: '2026-10-09T13:00:00Z', maskedMobile: '+201******679', codeLifetimeSeconds: 300 });
      await render();
      expect(root.textContent).toContain('public.wizard.verifyTitle');
      expect(root.querySelector<HTMLButtonElement>('button.link')?.disabled).toBe(true);
      // without a revealed code (an SMS gateway sends it) the page shows nothing but the masked mobile
      expect(root.querySelector('.code-reveal')).toBeNull();
    });

    it('shows the dial code of the chosen country beside each mobile and drops a typed leading 0 (LIS-CR-007 C-05)', async () => {
      setValue(root, 'select[formcontrolname=countryId]', 'eg');
      await render();
      backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.governorates).flush([]);
      await render();
      for (const name of ['mobile', 'managerMobile']) {
        expect(root.querySelector(`app-mobile-input[formcontrolname=${name}] .dial`)?.textContent).toContain('+20');
      }

      const input = root.querySelector<HTMLInputElement>('app-mobile-input[formcontrolname=mobile] input.number')!;
      setValue(root, 'app-mobile-input[formcontrolname=mobile] input.number', '01012345678');
      input.dispatchEvent(new Event('blur'));
      await render();
      expect(input.value).toBe('1012345678');
      expect(root.textContent).not.toContain('public.wizard.validation.mobileLength');
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
      setValue(root, 'app-mobile-input[formcontrolname=mobile] input.number', '01012345678');
      setValue(root, 'input[formcontrolname=managerName]', 'Ahmed Hassan');
      setValue(root, 'app-mobile-input[formcontrolname=managerMobile] input.number', '01012345679');
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

    it('offers city, district and area after the governorate and sends the picked locality (LIS-CR-008 C-04)', async () => {
      setValue(root, 'select[formcontrolname=countryId]', 'eg');
      await render();
      backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.governorates).flush([
        { id: 'cairo', countryId: 'eg', nameEn: 'Cairo', nameAr: 'القاهرة' },
        { id: 'giza', countryId: 'eg', nameEn: 'Giza', nameAr: 'الجيزة' },
      ]);
      await render();
      expect(root.querySelector('select[data-level=city]')).toBeNull();   // nothing before a governorate

      setValue(root, 'select[formcontrolname=governorateId]', 'cairo');
      await render();
      backend.expectOne((request) => request.url === '/api/v1/public/cities' && request.params.get('governorateId') === 'cairo').flush([{ id: 'nasr', governorateId: 'cairo', nameEn: 'Nasr City', nameAr: 'مدينة نصر' }]);
      await render();
      setValue(root, 'select[data-level=city]', 'nasr');
      await render();
      backend.expectOne((request) => request.url === '/api/v1/public/districts' && request.params.get('cityId') === 'nasr').flush([{ id: 'zone1', cityId: 'nasr', nameEn: 'First Zone', nameAr: 'المنطقة الأولى' }]);
      await render();
      setValue(root, 'select[data-level=district]', 'zone1');
      await render();
      backend.expectOne((request) => request.url === '/api/v1/public/areas' && request.params.get('districtId') === 'zone1').flush([{ id: 'block7', districtId: 'zone1', nameEn: 'Block 7', nameAr: 'بلوك 7' }]);
      await render();
      setValue(root, 'select[data-level=area]', 'block7');

      setValue(root, 'input[formcontrolname=labNameEn]', 'Al Noor Laboratories');
      setValue(root, 'input[formcontrolname=labNameAr]', 'مختبرات النور');
      setValue(root, 'input[formcontrolname=address]', 'Nasr City');
      setValue(root, 'app-mobile-input[formcontrolname=mobile] input.number', '01012345678');
      setValue(root, 'input[formcontrolname=managerName]', 'Ahmed Hassan');
      setValue(root, 'app-mobile-input[formcontrolname=managerMobile] input.number', '01012345679');
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      const start = backend.expectOne(PUBLIC_SUBSCRIPTION_API.start);
      expect(start.request.body).toMatchObject({ governorateId: 'cairo', cityId: 'nasr', districtId: 'zone1', areaId: 'block7' });
      start.flush({ type: 'about:blank', title: 'refused', status: 422, code: 'GEO-022' }, { status: 422, statusText: 'Unprocessable Entity' });
      await render();
      expect(root.querySelector('select[data-level=area]')?.classList.contains('ng-invalid')).toBe(true);   // GEO-022 marks the area

      // another governorate forgets the locality
      setValue(root, 'select[formcontrolname=governorateId]', 'giza');
      await render();
      backend.expectOne((request) => request.url === '/api/v1/public/cities' && request.params.get('governorateId') === 'giza').flush([]);
      await render();
      expect(root.querySelector('select[data-level=city]')).toBeNull();
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      expect(backend.expectOne(PUBLIC_SUBSCRIPTION_API.start).request.body).toMatchObject({ governorateId: 'giza', cityId: null, districtId: null, areaId: null });
    });

    describe('field-level refusals and the way back (LIS-CR-014)', () => {
      const LIVE = { subscriptionId: 'sub-1', expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(), maskedMobile: '+201******679', codeLifetimeSeconds: 300 };

      async function fillDetails(): Promise<void> {
        setValue(root, 'select[formcontrolname=countryId]', 'eg');
        await render();
        backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.governorates).flush([{ id: 'cairo', countryId: 'eg', nameEn: 'Cairo', nameAr: 'القاهرة' }]);
        await render();
        setValue(root, 'input[formcontrolname=labNameEn]', 'Al Noor Laboratories');
        setValue(root, 'input[formcontrolname=labNameAr]', 'مختبرات النور');
        setValue(root, 'select[formcontrolname=governorateId]', 'cairo');
        await render();
        backend.match((request) => request.url === '/api/v1/public/cities').forEach((request) => request.flush([]));
        setValue(root, 'input[formcontrolname=address]', 'Nasr City');
        setValue(root, 'app-mobile-input[formcontrolname=mobile] input.number', '01012345678');
        setValue(root, 'input[formcontrolname=managerName]', 'Ahmed Hassan');
        setValue(root, 'app-mobile-input[formcontrolname=managerMobile] input.number', '01012345679');
        await render();
      }

      function fieldOf(name: string): HTMLElement {
        const field = root.querySelector(`[formcontrolname=${name}]`)?.closest('app-form-field');
        if (!(field instanceof HTMLElement)) {
          throw new Error(`no field ${name}`);
        }

        return field;
      }

      async function submit(): Promise<void> {
        (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
        await render();
      }

      async function reachAccountStep(): Promise<void> {
        await fillDetails();
        await submit();
        backend.expectOne(PUBLIC_SUBSCRIPTION_API.start).flush(LIVE);
        await render();
        setValue(root, 'input[formcontrolname=code]', '123456');
        await submit();
        backend.expectOne(PUBLIC_SUBSCRIPTION_API.verify).flush(null, { status: 204, statusText: 'No Content' });
        await render();
        expect(root.querySelector('input[formcontrolname=username]')).not.toBeNull();
      }

      async function submitAccount(): Promise<void> {
        setValue(root, 'input[formcontrolname=username]', 'noor.admin');
        setValue(root, 'input[formcontrolname=password]', 'a long passphrase');
        setValue(root, 'input[formcontrolname=confirmation]', 'a long passphrase');
        await submit();
      }

      it('marks a mobile that already belongs to an account on its field when the user leaves it, before any code is sent', async () => {
        await fillDetails();
        root.querySelector('app-mobile-input[formcontrolname=managerMobile] input.number')?.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
        await render();
        const check = backend.expectOne(PUBLIC_SUBSCRIPTION_API.check);
        expect(check.request.body).toEqual({ countryId: 'eg', mobile: '+201012345678', managerMobile: '+201012345679' });
        check.flush({ issues: [{ field: 'managerMobile', code: 'SUB-011' }] });
        await render();
        await render();
        expect(fieldOf('managerMobile').classList.contains('invalid')).toBe(true);
        expect(fieldOf('mobile').classList.contains('invalid')).toBe(false);

        await submit();
        backend.expectNone(PUBLIC_SUBSCRIPTION_API.start); // the marked field holds the code back

        // start refuses the same way, naming the field
        setValue(root, 'app-mobile-input[formcontrolname=managerMobile] input.number', '01012345670');
        await submit();
        backend
          .expectOne(PUBLIC_SUBSCRIPTION_API.start)
          .flush({ type: 'about:blank', title: 'in use', status: 409, code: 'SUB-011', errors: { managerMobile: ['in use'] }, rules: { managerMobile: [{ rule: 'SUB-011' }] } }, { status: 409, statusText: 'Conflict' });
        await render();
        expect(fieldOf('managerMobile').classList.contains('invalid')).toBe(true);
        expect(root.querySelector('input[formcontrolname=code]')).toBeNull();
      });

      it('goes back from steps 2 and 3 with every value kept and resumes without a new code when nothing changed', async () => {
        await reachAccountStep();
        const rail = (): HTMLButtonElement[] => [...root.querySelectorAll<HTMLButtonElement>('app-wizard-rail button.step-link')];
        expect(rail().length).toBe(2); // steps 1 and 2 are done

        rail()[0]?.click();
        await render();
        expect(root.querySelector<HTMLInputElement>('input[formcontrolname=labNameEn]')?.value).toBe('Al Noor Laboratories');
        expect(root.textContent).toContain('public.wizard.sameDetails');
        expect(root.querySelector('form button[type=submit]')?.textContent).toContain('public.wizard.continue');

        await submit();
        backend.expectNone(PUBLIC_SUBSCRIPTION_API.start);
        expect(root.querySelector('input[formcontrolname=username]')).not.toBeNull(); // straight to step 3: the mobile is verified

        // the done step 2 of the rail shows the verified notice
        rail()[1]?.click();
        await render();
        expect(root.textContent).toContain('public.wizard.verifiedTitle');
        expect(root.querySelector('input[formcontrolname=code]')).toBeNull();

        // Back from there, change a detail: a new request and a new code
        [...root.querySelectorAll<HTMLButtonElement>('.wizard-actions button')].find((button) => button.textContent?.includes('public.wizard.back'))?.click();
        await render();
        setValue(root, 'input[formcontrolname=address]', '12 Tahrir St.');
        await render();
        expect(root.textContent).toContain('public.wizard.detailsChanged');
        await submit();
        expect(backend.expectOne(PUBLIC_SUBSCRIPTION_API.start).request.body).toMatchObject({ address: '12 Tahrir St.' });
      });

      it('has a Back button on step 3 that keeps the account values out of step 1', async () => {
        await reachAccountStep();
        const back = [...root.querySelectorAll<HTMLButtonElement>('.wizard-actions button')].find((button) => button.textContent?.includes('public.wizard.back'));
        expect(back).toBeDefined();
        back?.click();
        await render();
        expect(root.querySelector<HTMLInputElement>('input[formcontrolname=managerName]')?.value).toBe('Ahmed Hassan');
      });

      it('returns to step 1 with the mobile marked when step 3 refuses it, and sends a new code once it is changed', async () => {
        await reachAccountStep();
        await submitAccount();
        backend
          .expectOne(PUBLIC_SUBSCRIPTION_API.complete)
          .flush(
            { type: 'about:blank', title: 'in use', status: 409, code: 'SUB-011', errors: { managerMobile: ['in use'] }, rules: { managerMobile: [{ rule: 'SUB-011' }] } },
            { status: 409, statusText: 'Conflict' },
          );
        await render();
        await render();
        expect(root.querySelector('input[formcontrolname=labNameEn]')).not.toBeNull();
        expect(root.textContent).toContain('public.wizard.returnedForDetails');
        expect(fieldOf('managerMobile').classList.contains('invalid')).toBe(true);

        setValue(root, 'app-mobile-input[formcontrolname=managerMobile] input.number', '01012345670');
        await submit();
        expect(backend.expectOne(PUBLIC_SUBSCRIPTION_API.start).request.body).toMatchObject({ managerMobile: '+201012345670' });
      });

      it('shows a username refused by step 3 on the username field and stays on step 3', async () => {
        await reachAccountStep();
        await submitAccount();
        backend
          .expectOne(PUBLIC_SUBSCRIPTION_API.complete)
          .flush({ type: 'about:blank', title: 'taken', status: 409, code: 'SUB-013', errors: { username: ['taken'] } }, { status: 409, statusText: 'Conflict' });
        await render();
        expect(fieldOf('username').classList.contains('invalid')).toBe(true);
        expect(root.querySelector('.availability')?.getAttribute('data-state')).toBe('taken');
        expect(root.querySelector('input[formcontrolname=labNameEn]')).toBeNull();
      });
    });

    it('opens the sign-in address of the new laboratory from the last step, the username prefilled (LIS-CR-005)', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const facade = TestBed.inject(SubscriptionFacade);
      facade.completed.set({ tenantId: 't1', portalUrl: 'https://sky.example.com/lab/al-noor', username: 'noor.admin' });
      facade.step.set(4);
      await render();
      expect(root.querySelector('.portal-url a')?.getAttribute('href')).toBe('https://sky.example.com/lab/al-noor');

      (root.querySelector('.done button') as HTMLButtonElement).click();
      await render();
      expect(navigate).toHaveBeenCalledWith(['/lab', 'al-noor'], { queryParams: { username: 'noor.admin' } });
    });
  });
});

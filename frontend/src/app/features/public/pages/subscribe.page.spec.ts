import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PUBLIC_SUBSCRIPTION_API, type PublicCountry } from '../../../core/api/public-subscription.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
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

      setValue(root, 'input[formcontrolname=mobile]', '01012345678');
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
      await render();
      const start = backend.expectOne(PUBLIC_SUBSCRIPTION_API.start);
      expect(start.request.body).toMatchObject({ labNameEn: 'Al Noor Laboratories', countryId: 'eg', governorateId: 'cairo', mobile: '01012345678', licenceNo: null, email: null, language: 'en' });
      start.flush({ subscriptionId: 'sub-1', expiresAtUtc: '2026-10-09T13:00:00Z', maskedMobile: '+201******679', codeLifetimeSeconds: 300 });
      await render();
      expect(root.textContent).toContain('public.wizard.verifyTitle');
      expect(root.querySelector<HTMLButtonElement>('button.link')?.disabled).toBe(true);
    });
  });
});

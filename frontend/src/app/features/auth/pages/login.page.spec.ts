import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { ANNOUNCEMENT_API, type PublicAnnouncement } from '../../../core/api/announcement.contracts';
import { AUTH_API, type AuthProfile, type AuthResponse } from '../../../core/api/auth.contracts';
import { PUBLIC_LABORATORY_API, type PublicLaboratory } from '../../../core/api/public-laboratory.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PLATFORM_TENANT_ID } from '../../../core/tenancy/tenant-ids';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { bookmarkKeys } from '../ui/keep-address.component';
import { FindLaboratoryPage } from './find-laboratory.page';
import { LoginPage, SELECT_BRANCH_PATH } from './login.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const NILE: PublicLaboratory = { slug: 'nile-lab', nameEn: 'Nile Laboratory', nameAr: 'مختبر النيل', logoVersion: 3 };

function type(root: HTMLElement, selector: string, value: string): void {
  const input = root.querySelector<HTMLInputElement>(selector);
  if (!input) {
    throw new Error(`missing ${selector}`);
  }

  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function signedIn(profile: Partial<AuthProfile>): AuthResponse {
  return {
    step: 'signed-in',
    accessToken: 't',
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'User', tenantId: 't1', roleName: null, permissions: [], language: 'en', scope: 'full', branchId: null, branchIds: [], ...profile },
  };
}

describe('Sign-in addresses (CP-03, LIS-CR-005)', () => {
  let backend: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    // LIS-CR-013: a found laboratory also reads its SignIn announcements; tests that do not look at them answer none.
    backend.match((request) => request.url === ANNOUNCEMENT_API.publicList).forEach((request) => request.flush([]));
    backend.verify();
  });

  async function render(fixture: ComponentFixture<unknown>): Promise<void> {
    fixture.detectChanges();
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  async function laboratoryPage(slug: string, inputs: Readonly<Record<string, string>> = {}): Promise<ComponentFixture<LoginPage>> {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.componentRef.setInput('mode', 'laboratory');
    fixture.componentRef.setInput('slug', slug);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }

    await render(fixture);
    return fixture;
  }

  async function submit(fixture: ComponentFixture<unknown>, username: string): Promise<void> {
    const root = fixture.nativeElement as HTMLElement;
    type(root, '#username', username);
    type(root, '#password', 'secret-password');
    (root.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await settle();
  }

  describe('a laboratory address (/lab/:slug)', () => {
    it('lays out the 5c sign-in with the laboratory name and logo, keeps the idle notice and the prefill, and posts the slug', async () => {
      const fixture = await laboratoryPage('Nile-Lab', { reason: 'idle', username: 'lab.admin' });
      backend.expectOne(PUBLIC_LABORATORY_API.card('nile-lab')).flush(NILE);
      await render(fixture);
      const root = fixture.nativeElement as HTMLElement;

      expect(root.querySelector('.head .brand img')?.getAttribute('src')).toBe('/brand/sky-lis-logo.png');   // the Sky LIS logo stays in the header
      expect(root.querySelector('.hero h1')?.textContent).toContain('auth.hero.title');
      expect(root.querySelector('.card h2')?.textContent).toContain('auth.lab.title');
      expect(root.querySelector('.card .lab-logo')?.getAttribute('src')).toBe(PUBLIC_LABORATORY_API.logo('nile-lab', 3));
      expect(root.querySelector('.card .info-strip')?.textContent).toContain('auth.sessionEndedIdle');
      expect(root.querySelector('.card .forgot')?.getAttribute('href')).toBe('/lab/nile-lab/recover');
      expect(root.querySelector('.card .other a')?.getAttribute('href')).toBe('/login');
      expect([...root.querySelectorAll('.foot nav a')].map((link) => link.getAttribute('href'))).toEqual(['/welcome', '/welcome/subscribe']);
      expect(root.querySelector<HTMLInputElement>('#username')?.value).toBe('lab.admin');

      const eye = root.querySelector<HTMLButtonElement>('.card .eye');
      eye?.click();
      fixture.detectChanges();
      expect(root.querySelector<HTMLInputElement>('#password')?.type).toBe('text');
      expect(eye?.getAttribute('aria-pressed')).toBe('true');

      await submit(fixture, 'lab.user');
      const request = backend.expectOne(AUTH_API.signIn);
      expect(request.request.body).toEqual({ username: 'lab.user', password: 'secret-password', laboratory: 'nile-lab' });
      request.flush(signedIn({ portalSlug: 'nile-lab', branchIds: ['b1', 'b2'] }));
      await settle();
      await settle();

      // CP-04: a laboratory session without a branch picks one first
      expect(TestBed.inject(AuthStore).isAuthenticated()).toBe(true);
      expect(router.navigate).toHaveBeenCalledWith([SELECT_BRANCH_PATH], { queryParams: { returnUrl: '/' } });
    });

    it('shows "address not found" without a form for an unknown or inactive laboratory', async () => {
      const fixture = await laboratoryPage('closed-lab');
      backend.expectOne(PUBLIC_LABORATORY_API.card('closed-lab')).flush({ code: 'PLT-404', title: 'Not found', status: 404 }, { status: 404, statusText: 'Not Found' });
      await render(fixture);
      const root = fixture.nativeElement as HTMLElement;

      expect(root.querySelector('.missing h2')?.textContent).toContain('auth.lab.notFoundTitle');
      expect(root.querySelector('.missing a')?.getAttribute('href')).toBe('/login');
      expect(root.querySelector('form')).toBeNull();
    });

    it('does not even ask for a malformed address', async () => {
      const fixture = await laboratoryPage('x!');
      backend.expectNone(() => true);
      expect((fixture.nativeElement as HTMLElement).querySelector('.missing')).not.toBeNull();
    });

    it('keeps the form when the card cannot be read (the server decides at sign-in)', async () => {
      const fixture = await laboratoryPage('nile-lab');
      backend.expectOne(PUBLIC_LABORATORY_API.card('nile-lab')).flush(null, { status: 503, statusText: 'Unavailable' });
      await render(fixture);
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelector('.card h2')?.textContent).toContain('auth.title');
      expect(root.querySelector('form')).not.toBeNull();
    });
  });

  describe('the Super Admin address (/admin/login)', () => {
    async function adminPage(returnUrl?: string): Promise<ComponentFixture<LoginPage>> {
      const fixture = TestBed.createComponent(LoginPage);
      fixture.componentRef.setInput('mode', 'admin');
      if (returnUrl) {
        fixture.componentRef.setInput('returnUrl', returnUrl);
      }

      await render(fixture);
      return fixture;
    }

    it('shows the administration hero without a subscribe link and posts admin: true', async () => {
      const fixture = await adminPage('/account/dashboard');
      backend.expectNone(() => true);   // no laboratory card
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelector('.hero h1')?.textContent).toContain('auth.admin.title');
      expect(root.querySelector('.card h2')?.textContent).toContain('auth.admin.cardTitle');
      expect(root.querySelector('.card .forgot')?.getAttribute('href')).toBe('/admin/login/recover');
      expect(root.querySelector('.card .other')).toBeNull();
      expect([...root.querySelectorAll('.foot nav a')].map((link) => link.getAttribute('href'))).toEqual(['/welcome']);

      await submit(fixture, 'platform.admin');
      const request = backend.expectOne(AUTH_API.signIn);
      expect(request.request.body).toEqual({ username: 'platform.admin', password: 'secret-password', admin: true });
      request.flush(signedIn({ userId: 'a1', tenantId: PLATFORM_TENANT_ID, portalSlug: null }));
      await settle();
      await settle();

      // a return URL of the other portal is ignored: the platform administrator lands on the admin home
      expect(router.navigateByUrl).toHaveBeenCalledWith('/admin');
    });

    it('shows the refusal of a wrong password or a non-staff account as AUTH-002', async () => {
      const fixture = await adminPage();
      await submit(fixture, 'lab.user');
      backend.expectOne(AUTH_API.signIn).flush({ code: 'AUTH-002', title: 'Unauthorized', status: 401 }, { status: 401, statusText: 'Unauthorized' });
      await render(fixture);
      expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('auth.error');
      expect(TestBed.inject(AuthStore).isAuthenticated()).toBe(false);
    });
  });

  describe('Find your laboratory (/login)', () => {
    async function findPage(inputs: Readonly<Record<string, string>> = {}): Promise<ComponentFixture<FindLaboratoryPage>> {
      const fixture = TestBed.createComponent(FindLaboratoryPage);
      for (const [name, value] of Object.entries(inputs)) {
        fixture.componentRef.setInput(name, value);
      }

      await render(fixture);
      return fixture;
    }

    function continueWith(fixture: ComponentFixture<FindLaboratoryPage>, value: string): Promise<void> {
      const root = fixture.nativeElement as HTMLElement;
      type(root, '#laboratory-address', value);
      (root.querySelector('button[type=submit]') as HTMLButtonElement).click();
      return render(fixture);
    }

    it('asks only for the laboratory address, after the visible prefix, with the footer links', async () => {
      const fixture = await findPage({ reason: 'idle' });
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelector('.card h2')?.textContent).toContain('auth.find.title');
      expect(root.querySelector('.prefix')?.textContent).toContain('/lab/');
      expect(root.querySelector('#password')).toBeNull();
      expect(root.querySelector('.card .info-strip')?.textContent).toContain('auth.sessionEndedIdle');
      expect([...root.querySelectorAll('.foot nav a')].map((link) => link.getAttribute('href'))).toEqual(['/welcome', '/welcome/subscribe']);
    });

    it('refuses a value that is not an address and continues to /lab/<slug> with the return URL', async () => {
      const fixture = await findPage({ returnUrl: '/results/worklist' });
      await continueWith(fixture, 'ab');
      expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('auth.find.invalid');
      expect(router.navigate).not.toHaveBeenCalled();

      await continueWith(fixture, ' https://sky.example.com/lab/Nile-Lab ');
      expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')).toBeNull();
      expect(router.navigate).toHaveBeenCalledWith(['/lab', 'nile-lab'], { queryParams: { returnUrl: '/results/worklist' } });
    });

    it('LIS-CR-007 C-01: names the full address Continue opens, with the bookmark keys and a copy button, once the text is an address', async () => {
      const fixture = await findPage();
      const root = fixture.nativeElement as HTMLElement;
      type(root, '#laboratory-address', 'ab');
      await render(fixture);
      expect(root.querySelector('app-keep-address')).toBeNull();

      type(root, '#laboratory-address', 'Nile-Lab');
      await render(fixture);
      const hint = root.querySelector('form app-keep-address');
      expect(hint?.querySelector('.address')?.textContent).toBe(`${location.origin}/lab/nile-lab`);
      expect(hint?.textContent).toContain('auth.keepAddress.hint');
      expect(hint?.querySelector('.quiet')).toBeNull();   // no Hide on the hint
    });
  });

  describe('Keep this address (LIS-CR-007 C-01)', () => {
    let writeText: ReturnType<typeof vi.fn>;

    beforeEach(() => {
      writeText = vi.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    });

    afterEach(() => {
      Reflect.deleteProperty(navigator, 'clipboard');
    });

    it('shows the strip under the card of a found laboratory, copies the address and hides it for the page only', async () => {
      const storage = vi.spyOn(Storage.prototype, 'setItem');
      const toast = TestBed.inject(ToastService);
      const success = vi.spyOn(toast, 'success');
      const fixture = await laboratoryPage('nile-lab', { returnUrl: '/results/worklist' });
      backend.expectOne(PUBLIC_LABORATORY_API.card('nile-lab')).flush(NILE);
      await render(fixture);
      const root = fixture.nativeElement as HTMLElement;

      const strip = root.querySelector('.card-col > app-keep-address');
      expect(strip).not.toBeNull();
      expect(root.querySelector('.card app-keep-address')).toBeNull();   // under the card, not inside it
      expect(strip?.textContent).toContain('auth.keepAddress.strip');
      expect(strip?.querySelector('.address')?.textContent).toBe(`${location.origin}/lab/nile-lab`);   // without the query string

      strip?.querySelector<HTMLButtonElement>('.link-btn:not(.quiet)')?.click();
      await settle();
      expect(writeText).toHaveBeenCalledWith(`${location.origin}/lab/nile-lab`);
      expect(success).toHaveBeenCalledWith('auth.keepAddress.copied');

      strip?.querySelector<HTMLButtonElement>('.link-btn.quiet')?.click();
      await render(fixture);
      expect(root.querySelector('app-keep-address')).toBeNull();
      expect(storage).not.toHaveBeenCalled();
      expect(document.cookie).toBe('');
    });

    it('says so when the browser refuses the copy', async () => {
      writeText.mockRejectedValue(new Error('denied'));
      const failure = vi.spyOn(TestBed.inject(ToastService), 'error');
      const fixture = await laboratoryPage('nile-lab');
      backend.expectOne(PUBLIC_LABORATORY_API.card('nile-lab')).flush(NILE);
      await render(fixture);
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('app-keep-address .link-btn:not(.quiet)')?.click();
      await settle();
      expect(failure).toHaveBeenCalledWith('auth.keepAddress.copyFailed');
    });

    it('shows no strip on the Super Admin sign-in nor for an address that was not found', async () => {
      const admin = TestBed.createComponent(LoginPage);
      admin.componentRef.setInput('mode', 'admin');
      await render(admin);
      expect((admin.nativeElement as HTMLElement).querySelector('app-keep-address')).toBeNull();

      const missing = await laboratoryPage('closed-lab');
      backend.expectOne(PUBLIC_LABORATORY_API.card('closed-lab')).flush({ code: 'PLT-404', title: 'Not found', status: 404 }, { status: 404, statusText: 'Not Found' });
      await render(missing);
      expect((missing.nativeElement as HTMLElement).querySelector('app-keep-address')).toBeNull();
    });

    it('names the bookmark keys of the device', () => {
      expect(bookmarkKeys('Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0)')).toBe('⌘+D');
      expect(bookmarkKeys('Mozilla/5.0 (Windows NT 10.0; Win64; x64)')).toBe('Ctrl+D');
    });
  });
});

/**
 * LIS-CR-013 (C-17): the laboratory sign-in page shows the laboratory's live SignIn announcement in the hero instead of the
 * slogan (image, title, text, link), reports one impression, counts a click; without an announcement the slogan stays; the
 * Super Admin sign-in never asks.
 */
describe('Sign-in announcement (LIS-CR-013)', () => {
  let backend: HttpTestingController;

  const OFFER: PublicAnnouncement = { id: 'a1', titleEn: 'New analyser offer', titleAr: 'عرض الأجهزة', bodyEn: 'Ten percent off.', bodyAr: null, linkUrl: 'https://nationaltechnology.net/offer', imageVersion: 2 };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function open(mode: 'laboratory' | 'admin', announcements: readonly PublicAnnouncement[] | null): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('slug', 'nile-lab');
    fixture.detectChanges();
    TestBed.tick();
    await settle();
    if (mode === 'laboratory') {
      backend.expectOne(PUBLIC_LABORATORY_API.card('nile-lab')).flush(NILE);
      await settle();
      TestBed.tick();
      await settle();
      const list = backend.expectOne((request) => request.url === ANNOUNCEMENT_API.publicList);
      expect(list.request.params.get('placement')).toBe('SignIn');
      expect(list.request.params.get('laboratory')).toBe('nile-lab');
      if (announcements === null) {
        list.flush(null, { status: 503, statusText: 'Unavailable' });
      } else {
        list.flush(announcements);
      }

      await settle();
    }

    TestBed.tick();
    await settle();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('replaces the slogan with the announcement, counts one impression and a click on its link', async () => {
    const root = await open('laboratory', [OFFER]);
    backend.expectOne(ANNOUNCEMENT_API.impression('a1')).flush(null);

    const hero = root.querySelector('.hero') as HTMLElement;
    expect(hero.querySelector('h1')?.textContent).toContain('New analyser offer');
    expect(hero.textContent).toContain('Ten percent off.');
    expect(hero.textContent).not.toContain('auth.hero.title');
    expect(hero.querySelector('img')?.getAttribute('src')).toBe(ANNOUNCEMENT_API.publicImage('a1', 2));
    const link = hero.querySelector<HTMLAnchorElement>('a[target=_blank]');
    expect(link?.getAttribute('href')).toBe('https://nationaltechnology.net/offer');
    expect(link?.getAttribute('rel')).toContain('noopener');

    link?.addEventListener('click', (event) => event.preventDefault());
    link?.click();
    backend.expectOne(ANNOUNCEMENT_API.click('a1')).flush(null);
  });

  it('keeps the slogan when there is no announcement or the read fails', async () => {
    const none = await open('laboratory', []);
    expect(none.querySelector('.hero h1')?.textContent).toContain('auth.hero.title');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);

    const failed = await open('laboratory', null);
    expect(failed.querySelector('.hero h1')?.textContent).toContain('auth.hero.title');
  });

  it('the Super Admin sign-in asks for no announcement', async () => {
    const root = await open('admin', []);
    expect(root.querySelector('.hero h1')?.textContent).toContain('auth.admin.title');
    backend.expectNone((request) => request.url === ANNOUNCEMENT_API.publicList);
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AUTH_API, type AuthResponse } from '../core/api/auth.contracts';
import { AuthStore } from '../core/auth/auth.store';
import { I18nService } from '../core/i18n/i18n.service';
import { FakeBrowserNavigation, FakeSessionChannel, provideSessionTesting } from '../core/auth/session-testing';
import { PLATFORM_TENANT_ID } from '../core/tenancy/tenant-ids';
import { ShellComponent } from './shell.component';

async function signIn(tenantId: string, permissions: readonly string[], roleNameAr: string | null = null, names: { readonly displayNameEn?: string; readonly displayNameAr?: string } = {}): Promise<void> {
  const store = TestBed.inject(AuthStore);
  const backend = TestBed.inject(HttpTestingController);
  const pending = store.signIn('mona', 'pw', null);
  backend.expectOne(AUTH_API.signIn).flush({
    step: 'signed-in',
    accessToken: 't',
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'Mona Adel', ...names, tenantId, roleName: 'Cashier', roleNameAr, permissions, language: 'en', scope: 'full' },
    tenants: null,
  } satisfies AuthResponse);
  await pending;
}

function navLinks(host: HTMLElement): string[] {
  return [...host.querySelectorAll<HTMLAnchorElement>('.sb-scroll a.nav-item')].map((link) => link.getAttribute('href') ?? '');
}

describe('ShellComponent', () => {
  let navigation: FakeBrowserNavigation;
  let channel: FakeSessionChannel;

  beforeEach(async () => {
    navigation = new FakeBrowserNavigation();
    channel = new FakeSessionChannel();
    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting(), ...provideSessionTesting(navigation, channel)],
    }).compileComponents();
  });

  it('signs out with a full page load of the sign-in page, so no module keeps the user state (A-01, B-11)', async () => {
    await signIn('11111111-1111-1111-1111-111111111111', ['treasury.view']);
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    host.querySelector<HTMLButtonElement>('.profile')?.click();
    await fixture.whenStable();
    host.querySelector<HTMLButtonElement>('[role="menu"] .mi.danger')?.click();
    TestBed.inject(HttpTestingController).expectOne(AUTH_API.signOut).flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(navigation.loads).toEqual(['/login']);
    expect(channel.posted.at(-1)).toEqual({ kind: 'signed-out' });
    expect(TestBed.inject(AuthStore).isAuthenticated()).toBe(false);
  });

  it('shows the client navigation filtered by permission, with the user name and initials', async () => {
    await signIn('11111111-1111-1111-1111-111111111111', ['dashboard.view', 'treasury.view']);
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    // group order (overview … finance … account), then registry order inside a group
    expect(navLinks(host)).toEqual(['/account/dashboard', '/treasury/treasuries', '/treasury/access', '/account/profile']);
    expect(host.querySelector('.avatar')?.textContent?.trim()).toBe('MA');
    expect(host.querySelector('.who b')?.textContent?.trim()).toBe('Mona Adel');
  });

  it('hides entries whose permission the user lacks', async () => {
    await signIn('11111111-1111-1111-1111-111111111111', []);
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    // only the entries open to every signed-in user remain
    expect(navLinks(fixture.nativeElement as HTMLElement)).toEqual(['/account/profile']);
  });

  it('collapses and expands the sidebar from the menu button at the start of the top bar', async () => {
    await signIn('11111111-1111-1111-1111-111111111111', ['dashboard.view']);
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const button = host.querySelector<HTMLButtonElement>('.topbar .menu-btn');

    expect(host.querySelector('.collapse-btn')).toBeNull();   // no longer at the foot of the sidebar
    expect(button?.getAttribute('aria-expanded')).toBe('true');
    expect(button?.getAttribute('aria-label')).toBe('shell.collapseMenu');

    button?.click();
    await fixture.whenStable();
    expect(host.querySelector('.shell')?.classList.contains('collapsed')).toBe(true);
    expect(button?.getAttribute('aria-expanded')).toBe('false');
    expect(button?.getAttribute('aria-label')).toBe('shell.expandMenu');

    button?.click();
    await fixture.whenStable();
    expect(host.querySelector('.shell')?.classList.contains('collapsed')).toBe(false);
  });

  it('shows the admin navigation for the platform tenant', async () => {
    await signIn(PLATFORM_TENANT_ID, ['treasury.view']);
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    expect(navLinks(fixture.nativeElement as HTMLElement)).toEqual(['/admin', '/admin/profile']);
  });

  it('focuses the global search box on Ctrl+K when the page has none, and opens the user menu', async () => {
    await signIn('11111111-1111-1111-1111-111111111111', ['treasury.view']);
    const fixture = TestBed.createComponent(ShellComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    const event = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true });
    document.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(host.querySelector('.global-search input'));

    host.querySelector<HTMLButtonElement>('.profile')?.click();
    await fixture.whenStable();
    expect(host.querySelector('[role="menu"] .mi.danger')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    expect(host.querySelector('[role="menu"]')).toBeNull();

    host.remove();
  });

  it('names the role in the language of the page, in the sidebar and the top bar (validation v0.5.0 B-09)', async () => {
    await signIn('11111111-1111-1111-1111-111111111111', ['treasury.view'], 'أمين الصندوق');
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('aside.sidebar')?.textContent).toContain('Cashier');

    TestBed.inject(I18nService).setLanguage('ar');
    await fixture.whenStable();

    expect(host.querySelector('aside.sidebar')?.textContent).toContain('أمين الصندوق');
    expect(host.querySelector('aside.sidebar')?.textContent).not.toContain('Cashier');
    expect(host.textContent?.split('أمين الصندوق').length).toBeGreaterThan(2);   // the top bar too
  });

  it('names the user in the language of the page in the top bar (validation v0.5.0 R-12)', async () => {
    await signIn('11111111-1111-1111-1111-111111111111', ['treasury.view'], 'أمين الصندوق', { displayNameEn: 'Mona Adel', displayNameAr: 'منى عادل' });
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.who b')?.textContent?.trim()).toBe('Mona Adel');

    TestBed.inject(I18nService).setLanguage('ar');
    await fixture.whenStable();
    expect(host.querySelector('.who b')?.textContent?.trim()).toBe('منى عادل');
    expect(host.textContent).not.toContain('Mona Adel');

    TestBed.inject(I18nService).setLanguage('en');
    await fixture.whenStable();
    expect(host.querySelector('.who b')?.textContent?.trim()).toBe('Mona Adel');
  });
});

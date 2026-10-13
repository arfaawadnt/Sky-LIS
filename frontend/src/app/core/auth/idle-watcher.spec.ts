import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AUTH_API, type AuthResponse } from '../api/auth.contracts';
import { AuthStore } from './auth.store';
import { IdleWarningComponent } from './idle-warning.component';
import { IdleWatcher, renewalIntervalMs, warningLeadMs } from './idle-watcher';
import { readSessionMessage } from './session-channel';
import { FakeBrowserNavigation, FakeSessionChannel, provideSessionTesting } from './session-testing';

function signedIn(token: string, sessionTimeoutMinutes: number | null = 2): AuthResponse {
  return {
    step: 'signed-in',
    accessToken: token,
    expiresAtUtc: null,
    refreshToken: null,
    profile: {
      userId: 'u1',
      displayName: 'Mona Adel',
      tenantId: 't1',
      roleName: null,
      permissions: [],
      language: 'en',
      scope: 'full',
      ...(sessionTimeoutMinutes === null ? {} : { sessionTimeoutMinutes }),
    },
    tenants: null,
  };
}

/** Lets the promise chains of a flushed request run (fake timers leave microtasks alone). */
async function settle(): Promise<void> {
  for (let i = 0; i < 20; i++) {
    await Promise.resolve();
  }
}

function seconds(count: number): void {
  vi.advanceTimersByTime(count * 1000);
}

describe('IdleWatcher (validation F-01: the tenant idle timeout)', () => {
  let store: AuthStore;
  let watcher: IdleWatcher;
  let http: HttpTestingController;
  let navigation: FakeBrowserNavigation;
  let channel: FakeSessionChannel;

  /** Signs in with a 2-minute timeout: warning at 60 s, end at 120 s, renewal after 30 s of staleness. */
  async function start(timeout: number | null = 2): Promise<void> {
    const pending = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1', timeout));
    await pending;
    watcher.start();
    watcher.check();
    channel.posted.length = 0;
  }

  async function expectIdleSignOut(): Promise<void> {
    http.expectOne(AUTH_API.signOut).flush(null);
    await settle();
    expect(navigation.loads).toEqual(['/login?reason=idle']);
    expect(channel.posted).toContainEqual({ kind: 'signed-out', reason: 'idle' });
  }

  beforeEach(() => {
    vi.useFakeTimers({ now: new Date('2026-10-12T08:00:00Z') });
    navigation = new FakeBrowserNavigation();
    channel = new FakeSessionChannel();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), ...provideSessionTesting(navigation, channel)],
    });
    store = TestBed.inject(AuthStore);
    watcher = TestBed.inject(IdleWatcher);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('takes the timeout from the session profile, 30 minutes when the server sent none', async () => {
    await start(null);   // no sessionTimeoutMinutes in the profile
    expect(watcher.timeoutMs()).toBe(30 * 60_000);
    expect(warningLeadMs(30 * 60_000)).toBe(60_000);
    expect(warningLeadMs(60_000)).toBe(30_000);   // a one-minute timeout warns at half of it
    expect(renewalIntervalMs(30 * 60_000)).toBe(60_000);
    expect(renewalIntervalMs(60_000)).toBe(15_000);
  });

  it('shows the warning a minute before the end, then signs out at the timeout to the sign-in page with the notice', async () => {
    await start();
    expect(watcher.timeoutMs()).toBe(120_000);

    seconds(59);
    expect(watcher.warning()).toBe(false);
    seconds(2);
    expect(watcher.warning()).toBe(true);
    expect(watcher.secondsLeft()).toBe(59);
    http.expectNone(AUTH_API.signOut);

    seconds(59);
    expect(watcher.warning()).toBe(false);
    await expectIdleSignOut();
    expect(store.hasEnded()).toBe(true);
  });

  it('local activity restarts the count, is told to the other tabs and renews a stale session on the server', async () => {
    await start();

    seconds(10);
    document.dispatchEvent(new MouseEvent('mousemove'));
    expect(channel.posted).toEqual([{ kind: 'activity', userId: 'u1', tenantId: 't1' }]);
    http.expectNone(AUTH_API.refresh);   // renewed 10 s ago: nothing to renew yet

    seconds(40);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }));
    http.expectOne(AUTH_API.refresh).flush(signedIn('token-2'));   // the last renewal is 50 s old
    await settle();
    expect(channel.posted).toContainEqual({ kind: 'renewed', userId: 'u1', tenantId: 't1' });
    expect(store.token()).toBe('token-2');

    seconds(55);   // t = 105 s: the first count would be warning since 60 s
    expect(watcher.warning()).toBe(false);
    seconds(20);   // t = 125 s: past the end the first count had; the warning of the new count shows
    expect(watcher.warning()).toBe(true);
    http.expectNone(AUTH_API.signOut);
    expect(navigation.loads).toEqual([]);

    seconds(45);   // t = 170 s: two minutes after the last activity
    await expectIdleSignOut();
  });

  it('activity in another tab keeps this one alive, and closes its warning', async () => {
    await start();

    seconds(50);
    channel.deliver({ kind: 'activity', userId: 'u1', tenantId: 't1' });
    channel.deliver({ kind: 'renewed', userId: 'u1', tenantId: 't1' });   // that tab renewed the session on the server
    seconds(70);   // t = 120 s: this tab's own count would have ended
    expect(watcher.warning()).toBe(true);   // 50 s left of the count the other tab started
    http.expectNone(AUTH_API.signOut);

    channel.deliver({ kind: 'activity', userId: 'u2', tenantId: 't1' });   // another identity's activity is not this tab's
    channel.deliver({ kind: 'activity', userId: 'u1', tenantId: 't1' });
    channel.deliver({ kind: 'renewed', userId: 'u1', tenantId: 't1' });
    expect(watcher.warning()).toBe(false);

    seconds(119);
    http.expectNone(AUTH_API.signOut);
    seconds(1);
    await expectIdleSignOut();
  });

  it('the warning asks for an answer: movement does not close it, Stay signed in refreshes and does', async () => {
    await start();
    seconds(61);
    expect(watcher.warning()).toBe(true);

    document.dispatchEvent(new MouseEvent('mousemove'));
    expect(watcher.warning()).toBe(true);
    http.expectNone(AUTH_API.refresh);

    watcher.stay();
    expect(watcher.warning()).toBe(false);
    http.expectOne(AUTH_API.refresh).flush(signedIn('token-2'));
    await settle();

    seconds(30);
    expect(watcher.warning()).toBe(false);
    expect(navigation.loads).toEqual([]);
  });

  it('the root dialog shows the warning and its Stay signed in button renews the session', async () => {
    await start();
    const fixture = TestBed.createComponent(IdleWarningComponent);
    const host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    expect(host.querySelector('[role="dialog"]')).toBeNull();

    seconds(61);
    fixture.detectChanges();
    expect(host.querySelector('[role="dialog"] h2')?.textContent?.trim()).toBe('shared.session.idleTitle');
    host.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    http.expectOne(AUTH_API.refresh).flush(signedIn('token-2'));
    await settle();
    fixture.detectChanges();
    expect(host.querySelector('[role="dialog"]')).toBeNull();
  });

  it('Sign out in the warning is an ordinary sign-out', async () => {
    await start();
    seconds(61);

    watcher.signOut();
    http.expectOne(AUTH_API.signOut).flush(null);
    await settle();
    expect(navigation.loads).toEqual(['/login']);
    expect(channel.posted).toContainEqual({ kind: 'signed-out' });
  });

  it('a refresh the server refuses after inactivity (AUTH-017) lands every tab on the sign-in page with the notice', async () => {
    await start();

    const refreshed = store.refresh();
    http.expectOne(AUTH_API.refresh).flush({ code: 'AUTH-017', title: 'Your session ended after inactivity.', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    expect(await refreshed).toBeNull();
    expect(navigation.loads).toEqual(['/login?reason=idle']);
    expect(channel.posted).toContainEqual({ kind: 'signed-out', reason: 'idle' });
  });

  it('an idle sign-out in another tab ends this one with the notice too', async () => {
    await start();
    channel.deliver({ kind: 'signed-out', reason: 'idle' });
    expect(navigation.loads).toEqual(['/login?reason=idle']);
    expect(store.hasEnded()).toBe(true);
  });

  it('reads the new channel messages from other tabs strictly', () => {
    expect(readSessionMessage({ kind: 'activity', userId: 'u', tenantId: null })).toEqual({ kind: 'activity', userId: 'u', tenantId: null });
    expect(readSessionMessage({ kind: 'renewed', userId: 'u', tenantId: 't', at: 1 })).toEqual({ kind: 'renewed', userId: 'u', tenantId: 't' });
    expect(readSessionMessage({ kind: 'activity', userId: 1, tenantId: null })).toBeNull();
    expect(readSessionMessage({ kind: 'signed-out', reason: 'idle' })).toEqual({ kind: 'signed-out', reason: 'idle' });
    expect(readSessionMessage({ kind: 'signed-out', reason: 'other' })).toEqual({ kind: 'signed-out' });
  });
});

import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AUTH_API, type AuthResponse } from '../api/auth.contracts';
import { AuthStore } from './auth.store';
import { REFRESH_LOCK, REFRESH_LOCK_NAME, type RefreshLock, browserRefreshLock, directRefreshLock } from './refresh-lock';
import { FakeBrowserNavigation, provideSessionTesting } from './session-testing';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** A Web Locks double with one exclusive lock per name and a FIFO queue, like the browser's. */
class FakeLockManager {
  readonly requested: string[] = [];
  private tail: Promise<unknown> = Promise.resolve();
  refuse: Error | null = null;

  request(name: string, options: { signal?: AbortSignal }, callback: () => Promise<unknown>): Promise<unknown> {
    this.requested.push(name);
    if (this.refuse !== null) {
      return Promise.reject(this.refuse);
    }

    const run = this.tail.then(() => {
      if (options.signal?.aborted) {
        throw new DOMException('aborted', 'AbortError');
      }

      return callback();
    });
    this.tail = run.catch(() => undefined);
    return run;
  }
}

function asLocks(fake: FakeLockManager): LockManager {
  return fake as unknown as LockManager;
}

function signedIn(token: string): AuthResponse {
  return {
    step: 'signed-in',
    accessToken: token,
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions: [], language: 'en', scope: 'full', branchId: null, branchIds: [] },
    tenants: null,
  };
}

describe('Refresh lock across tabs (C-02)', () => {
  it('runs one task at a time under the shared name', async () => {
    const locks = new FakeLockManager();
    const lock = browserRefreshLock(asLocks(locks));
    const order: string[] = [];
    let releaseFirst: () => void = () => undefined;

    const first = lock(() => new Promise<string>((resolve) => {
      order.push('first:start');
      releaseFirst = () => {
        order.push('first:end');
        resolve('one');
      };
    }));
    const second = lock(async () => {
      order.push('second:start');
      return 'two';
    });

    await settle();
    expect(order).toEqual(['first:start']);   // the second tab waits for the first tab's refresh
    releaseFirst();
    expect(await first).toBe('one');
    expect(await second).toBe('two');
    expect(order).toEqual(['first:start', 'first:end', 'second:start']);
    expect(locks.requested).toEqual([REFRESH_LOCK_NAME, REFRESH_LOCK_NAME]);
  });

  it('refreshes anyway when the lock is not granted, and never runs a started task twice', async () => {
    const locks = new FakeLockManager();
    locks.refuse = new DOMException('aborted', 'AbortError');
    const task = vi.fn(async () => 'ran');
    expect(await browserRefreshLock(asLocks(locks))(task)).toBe('ran');
    expect(task).toHaveBeenCalledTimes(1);

    locks.refuse = null;
    const failing = vi.fn(async () => {
      throw new Error('network');
    });
    await expect(browserRefreshLock(asLocks(locks))(failing)).rejects.toThrow('network');
    expect(failing).toHaveBeenCalledTimes(1);
  });

  it('runs directly where the Web Locks API is missing', () => {
    expect(browserRefreshLock(undefined)).toBe(directRefreshLock);
  });

  it('sends the refresh request only while holding the lock', async () => {
    const events: string[] = [];
    let release: () => void = () => undefined;
    const recordingLock: RefreshLock = async (task) => {
      events.push('waiting');
      await new Promise<void>((resolve) => {
        release = resolve;
      });
      events.push('granted');
      return task();
    };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        ...provideSessionTesting(new FakeBrowserNavigation()),
        { provide: REFRESH_LOCK, useValue: recordingLock },
      ],
    });
    const store = TestBed.inject(AuthStore);
    const http = TestBed.inject(HttpTestingController);

    const pending = store.refresh();
    await settle();
    expect(events).toEqual(['waiting']);
    http.expectNone(AUTH_API.refresh);   // another tab is refreshing: this one has not sent its (stale) cookie

    release();
    await settle();
    http.expectOne(AUTH_API.refresh).flush(signedIn('token-after-wait'));
    expect(await pending).toBe('token-after-wait');
    expect(events).toEqual(['waiting', 'granted']);
    http.verify();
  });
});

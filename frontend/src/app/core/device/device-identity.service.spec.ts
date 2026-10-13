import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AuthStore, type CurrentUser } from '../auth/auth.store';
import { DeviceIdentityService } from './device-identity.service';

const user = (id: string): CurrentUser => ({ id, displayName: id, language: 'en', scope: 'tenant' });

/** Validation v0.5.0 O-10: one device id per user and browser profile, so a treasury approval binds the person on the device. */
describe('DeviceIdentityService', () => {
  const current = signal<CurrentUser | null>(null);

  function create(): DeviceIdentityService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: AuthStore, useValue: { currentUser: current } satisfies Pick<AuthStore, 'currentUser'> }],
    });
    return TestBed.inject(DeviceIdentityService);
  }

  function stored(): Record<string, string> {
    return JSON.parse(window.localStorage.getItem(DeviceIdentityService.KEY) ?? '{}') as Record<string, string>;
  }

  beforeEach(() => {
    window.localStorage.clear();
    current.set(null);
  });

  afterEach(() => {
    window.localStorage.clear();
  });

  it('gives each user of the browser their own id and keeps it across page loads, in one storage entry', () => {
    const service = create();
    current.set(user('u-anna'));
    const anna = service.deviceId();
    current.set(user('u-omar'));
    const omar = service.deviceId();

    expect(anna).not.toBe(omar);
    expect(window.localStorage.length).toBe(1);
    expect(stored()).toEqual({ 'u-anna': anna, 'u-omar': omar });

    const reloaded = create();
    current.set(user('u-anna'));
    expect(reloaded.deviceId()).toBe(anna);
    current.set(user('u-omar'));
    expect(reloaded.deviceId()).toBe(omar);
  });

  it('hands the shared id written before O-10 to the first user who asks, and a new one to the next', () => {
    window.localStorage.setItem(DeviceIdentityService.KEY, '3757857f-0000-4000-8000-000000000001');
    const service = create();

    current.set(user('u-first'));
    expect(service.deviceId()).toBe('3757857f-0000-4000-8000-000000000001');
    current.set(user('u-second'));
    const second = service.deviceId();

    expect(second).not.toBe('3757857f-0000-4000-8000-000000000001');
    expect(stored()).toEqual({ 'u-first': '3757857f-0000-4000-8000-000000000001', 'u-second': second });
  });

  it('stores nothing without a signed-in user and keeps the map bounded', () => {
    const service = create();
    const anonymous = service.deviceId();
    expect(service.deviceId()).toBe(anonymous);
    expect(window.localStorage.length).toBe(0);

    const many = Object.fromEntries(Array.from({ length: DeviceIdentityService.MAX_USERS }, (_, index) => [`u-${index}`, `d-${index}`]));
    window.localStorage.setItem(DeviceIdentityService.KEY, JSON.stringify(many));
    const bounded = create();
    current.set(user('u-new'));
    bounded.deviceId();

    const map = stored();
    expect(Object.keys(map).length).toBe(DeviceIdentityService.MAX_USERS);
    expect(map['u-0']).toBeUndefined();
    expect(map['u-1']).toBe('d-1');
    expect(map['u-new']).toBeDefined();
  });
});

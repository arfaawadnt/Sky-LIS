import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AUTH_API, type AuthResponse } from '../../core/api/auth.contracts';
import { AuthStore } from '../../core/auth/auth.store';
import { CanDirective } from './can.directive';

@Component({
  imports: [CanDirective],
  template: `<button *appCan="'treasury.approve'" id="approve">Approve</button><span *appCan="'treasury.view'" id="view">View</span>`,
})
class HostComponent {}

describe('appCan directive (pitfall P-20: affordance only)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders only what the signed-in user may do and reacts when the session changes', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('#approve')).toBeNull();
    expect(host.querySelector('#view')).toBeNull();

    const store = TestBed.inject(AuthStore);
    const backend = TestBed.inject(HttpTestingController);
    const signIn = store.signIn('mona', 'pw', null);
    backend.expectOne(AUTH_API.signIn).flush({
      step: 'signed-in',
      accessToken: 't',
      expiresAtUtc: null,
      refreshToken: null,
      profile: { userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: 'Cashier', permissions: ['treasury.view'], language: 'en', scope: 'full' },
      tenants: null,
    } satisfies AuthResponse);
    await signIn;
    await fixture.whenStable();

    expect(host.querySelector('#view')).not.toBeNull();
    expect(host.querySelector('#approve')).toBeNull();

    store.clear();
    await fixture.whenStable();
    expect(host.querySelector('#view')).toBeNull();
  });
});

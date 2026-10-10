import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AUTH_API, type AuthResponse } from '../../../core/api/auth.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { LoginPage, SELECT_BRANCH_PATH } from './login.page';
import { RecoveryPage } from './recovery.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function type(root: HTMLElement, selector: string, value: string): void {
  const input = root.querySelector<HTMLInputElement>(selector);
  if (!input) {
    throw new Error(`missing ${selector}`);
  }

  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('Sign-in and password recovery (CP-03, CP-04 entry)', () => {
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

  describe('recovery', () => {
    let fixture: ComponentFixture<RecoveryPage>;
    let root: HTMLElement;

    async function render(): Promise<void> {
      fixture.detectChanges();
      await settle();
      fixture.detectChanges();
    }

    function submit(): void {
      (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
    }

    beforeEach(async () => {
      fixture = TestBed.createComponent(RecoveryPage);
      root = fixture.nativeElement as HTMLElement;
      await render();
    });

    it('sends the code, shows AUTH-013 as an invalid code, and ends on the sign-in page with a toast', async () => {
      const toast = vi.spyOn(TestBed.inject(ToastService), 'success');
      type(root, 'input[autocomplete=username]', 'lab.admin');
      submit();
      await render();
      const start = backend.expectOne(AUTH_API.recoveryStart);
      expect(start.request.body).toEqual({ usernameOrMobile: 'lab.admin', language: 'en' });
      start.flush(null, { status: 202, statusText: 'Accepted' });
      await render();

      type(root, 'input.otp', '12345');
      submit();
      await render();
      expect(root.querySelector('input.otp')).not.toBeNull();   // five digits stay on the code step

      type(root, 'input.otp', '123456');
      submit();
      await render();
      type(root, 'input[formcontrolname=password]', 'my-lab.admin-key');
      type(root, 'input[formcontrolname=confirmation]', 'my-lab.admin-key');
      submit();
      await render();
      backend.expectNone(AUTH_API.recoveryComplete);   // contains the username

      type(root, 'input[formcontrolname=password]', 'a quiet morning walk');
      type(root, 'input[formcontrolname=confirmation]', 'a quiet morning walk');
      submit();
      await render();
      const refused = backend.expectOne(AUTH_API.recoveryComplete);
      expect(refused.request.body).toEqual({ usernameOrMobile: 'lab.admin', code: '123456', newPassword: 'a quiet morning walk' });
      refused.flush({ code: 'AUTH-013', title: 'Rejected', status: 401 }, { status: 401, statusText: 'Unauthorized' });
      await render();
      expect(root.querySelector('input.otp')).not.toBeNull();   // back to the code step
      expect(root.querySelector('[role=alert]')).not.toBeNull();   // the dictionary is not loaded here: the key falls back to the generic text

      type(root, 'input.otp', '654321');
      submit();
      await render();
      type(root, 'input[formcontrolname=password]', 'a quiet morning walk');
      type(root, 'input[formcontrolname=confirmation]', 'a quiet morning walk');
      submit();
      await render();
      backend.expectOne(AUTH_API.recoveryComplete).flush(null, { status: 204, statusText: 'No Content' });
      await render();
      expect(toast).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { username: 'lab.admin' } });
    });
  });

  it('sends a laboratory session without a branch to branch selection after sign-in', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
    type(root, '#username', 'lab.user');
    type(root, '#password', 'secret-password');
    (root.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await settle();

    // The envelope interceptor is not installed here: the request goes out as the store sends it.
    backend.expectOne(AUTH_API.signIn).flush({
      step: 'signed-in',
      accessToken: 't',
      expiresAtUtc: null,
      refreshToken: null,
      profile: { userId: 'u1', displayName: 'Lab User', tenantId: '11111111-1111-1111-1111-111111111111', roleName: null, permissions: [], language: 'en', scope: 'full', branchId: null, branchIds: ['b1', 'b2'] },
      tenants: null,
    } satisfies AuthResponse);
    await settle();
    await settle();

    expect(TestBed.inject(AuthStore).isAuthenticated()).toBe(true);
    expect(router.navigate).toHaveBeenCalledWith([SELECT_BRANCH_PATH], { queryParams: { returnUrl: '/' } });
  });
});

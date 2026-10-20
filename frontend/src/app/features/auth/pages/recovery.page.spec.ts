import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AUTH_API } from '../../../core/api/auth.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { ToastService } from '../../../shared/ui/toast/toast.service';
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

describe('Password recovery (CP-03)', () => {
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
    it('clears the previous refusal when the user goes back and asks for a new code (C-06)', async () => {
      type(root, 'input[autocomplete=username]', 'lab.admin');
      submit();
      await render();
      backend.expectOne(AUTH_API.recoveryStart).flush(null, { status: 202, statusText: 'Accepted' });
      await render();
      type(root, 'input.otp', '123456');
      submit();
      await render();
      type(root, 'input[formcontrolname=password]', 'a quiet morning walk');
      type(root, 'input[formcontrolname=confirmation]', 'a quiet morning walk');
      submit();
      await render();
      backend.expectOne(AUTH_API.recoveryComplete).flush({ code: 'AUTH-013', title: 'Rejected', status: 401 }, { status: 401, statusText: 'Unauthorized' });
      await render();
      expect(root.querySelector('[role=alert]')).not.toBeNull();

      (root.querySelector('.actions button[type=button]') as HTMLButtonElement).click();   // Back
      await render();
      expect(root.querySelector('input.otp')).toBeNull();
      type(root, 'input[autocomplete=username]', 'other.user');
      submit();
      await render();
      backend.expectOne(AUTH_API.recoveryStart).flush(null, { status: 202, statusText: 'Accepted' });
      await render();
      expect(root.querySelector('input.otp')).not.toBeNull();
      expect(root.querySelector('[role=alert]')).toBeNull();
      expect((root.querySelector('input.otp') as HTMLInputElement).value).toBe('');
    });
  });

  it('returns to the laboratory sign-in it was started from (LIS-CR-005)', async () => {
    const fixture = TestBed.createComponent(RecoveryPage);
    fixture.componentRef.setInput('mode', 'laboratory');
    fixture.componentRef.setInput('slug', 'nile-lab');
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    expect(root.querySelector('.foot a')?.getAttribute('href')).toBe('/lab/nile-lab');

    fixture.componentRef.setInput('mode', 'admin');
    fixture.detectChanges();
    expect(root.querySelector('.foot a')?.getAttribute('href')).toBe('/admin/login');
  });
});

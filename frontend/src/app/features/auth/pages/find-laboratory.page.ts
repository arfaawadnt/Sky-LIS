import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { IDLE_REASON, LABORATORY_PATH_PREFIX, isLaboratorySlug, meaningfulReturnUrl, normaliseLaboratorySlug } from '../../../core/auth/session-navigation';
import { I18nService } from '../../../core/i18n/i18n.service';
import { AuthFrameComponent } from '../ui/auth-frame.component';
import { KeepAddressComponent } from '../ui/keep-address.component';

/**
 * Find your laboratory (LIS-CR-005), at `/login` and the anonymous root: every laboratory signs in at its own address
 * (`/lab/<slug>`), so this page asks only for that address — typed after the visible prefix `<host>/lab/`, or pasted whole —
 * and continues there after a shape check; the laboratory page itself answers whether the laboratory exists. There is
 * no username or password here. `returnUrl` and `username` travel on to the laboratory page; `?reason=idle` (F-01) says
 * why the session ended when it ended without a known address. LIS-CR-007 C-01: once the typed text is an address, a hint
 * under Continue names the full address it opens, with the bookmark keys and *Copy address* (nothing stored on the device).
 */
@Component({
  selector: 'app-find-laboratory-page',
  imports: [ReactiveFormsModule, AuthFrameComponent, KeepAddressComponent],
  templateUrl: './find-laboratory.page.html',
  styleUrl: './find-laboratory.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FindLaboratoryPage {
  protected readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

  readonly returnUrl = input<string | undefined>();
  readonly reason = input<string | undefined>();
  readonly username = input<string | undefined>();

  protected readonly endedIdle = computed(() => this.reason() === IDLE_REASON);

  /** The visible prefix of the field: `sky.example.com/lab/`. */
  protected readonly prefix = `${inject(DOCUMENT).defaultView?.location.host ?? ''}${LABORATORY_PATH_PREFIX}/`;

  protected readonly address = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(256)] });

  protected readonly form = new FormGroup({ address: this.address });

  private readonly origin = inject(DOCUMENT).defaultView?.location.origin ?? '';
  private readonly typed = toSignal(this.address.valueChanges, { initialValue: this.address.value });

  /** The full address Continue opens, once the typed text has the shape of one. */
  protected readonly foundAddress = computed(() => {
    const slug = normaliseLaboratorySlug(this.typed());
    return isLaboratorySlug(slug) ? `${this.origin}${LABORATORY_PATH_PREFIX}/${slug}` : null;
  });

  /** True once Continue found a value that is not an address. */
  protected readonly invalid = signal(false);

  constructor() {
    void this.i18n.load('auth');
  }

  protected async continue(): Promise<void> {
    const slug = normaliseLaboratorySlug(this.address.value);
    if (!isLaboratorySlug(slug)) {
      this.address.markAsTouched();
      this.invalid.set(true);
      return;
    }

    this.invalid.set(false);
    const returnUrl = meaningfulReturnUrl(this.returnUrl());
    const username = this.username();
    await this.router.navigate([LABORATORY_PATH_PREFIX, slug], {
      queryParams: { ...(returnUrl === null ? {} : { returnUrl }), ...(username ? { username } : {}) },
    });
  }
}

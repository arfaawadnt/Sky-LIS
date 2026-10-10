import { DestroyRef, Injectable, computed, inject, resource, signal } from '@angular/core';

import type {
  PublicCountry,
  StartSubscriptionRequest,
  SubscriptionCompletedResponse,
  SubscriptionStarted,
  UsernameAvailability,
} from '../../../core/api/public-subscription.contracts';
import { mutation } from '../../../core/state/mutation';
import { PublicSubscriptionApi } from '../data/public-subscription.api';
import { isUsernameShapeValid, normaliseUsername } from '../data/subscription-rules';

/** The four steps of CP-02: laboratory details, the code sent to the manager's mobile, the administrator's credentials, the portal address. */
export type WizardStep = 1 | 2 | 3 | 4;

export type UsernameState = 'idle' | 'checking' | 'invalid' | 'available' | 'taken' | 'error';

/** Seconds before "send the code again" is offered (CP-02: resend after 60 s). */
export const RESEND_AFTER_SECONDS = 60;

const USERNAME_CHECK_DELAY_MS = 400;

/**
 * State of the subscription wizard (01 §6.3): reference lists through `resource()`, the four calls through
 * `mutation()`, the step and the resend countdown in signals. Provided at the `subscribe` route, so leaving the page
 * forgets everything (nothing is kept in web storage).
 */
@Injectable()
export class SubscriptionFacade {
  private readonly api = inject(PublicSubscriptionApi);
  private readonly destroyRef = inject(DestroyRef);
  private usernameTimer: ReturnType<typeof setTimeout> | null = null;
  private usernameSequence = 0;
  private tick: ReturnType<typeof setInterval> | null = null;

  readonly step = signal<WizardStep>(1);
  readonly countryId = signal<string | null>(null);
  readonly started = signal<SubscriptionStarted | null>(null);
  readonly completed = signal<SubscriptionCompletedResponse | null>(null);
  readonly usernameState = signal<UsernameState>('idle');
  private readonly resendAt = signal(0);
  private readonly now = signal(Date.now());

  readonly countries = resource({ loader: () => this.api.countries() });

  readonly governorates = resource({
    params: () => this.countryId() ?? undefined,
    loader: ({ params }) => this.api.governorates(params),
  });

  readonly country = computed<PublicCountry | null>(() => {
    const id = this.countryId();
    return (this.countries.hasValue() ? this.countries.value() : []).find((country) => country.id === id) ?? null;
  });

  /** Whole seconds until the code may be sent again; 0 when it may. */
  readonly resendIn = computed(() => Math.max(0, Math.ceil((this.resendAt() - this.now()) / 1000)));

  /** Minutes the code stays valid (CP-02: five). */
  readonly codeMinutes = computed(() => Math.max(1, Math.round((this.started()?.codeLifetimeSeconds ?? 300) / 60)));

  readonly start = mutation((request: StartSubscriptionRequest) => this.api.start(request), {
    then: (started) => {
      this.started.set(started);
      this.armResend();
      this.step.set(2);
    },
  });

  readonly resend = mutation((subscriptionId: string) => this.api.resend(subscriptionId), {
    then: (started) => {
      this.started.set(started);
      this.armResend();
    },
  });

  readonly verify = mutation((input: { subscriptionId: string; code: string }) => this.api.verify(input.subscriptionId, input.code), {
    then: () => this.step.set(3),
  });

  readonly complete = mutation((input: { subscriptionId: string; username: string; password: string }) => this.api.complete(input), {
    then: (completed) => {
      this.completed.set(completed);
      this.stopTicking();
      this.step.set(4);
    },
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.stopTicking();
      if (this.usernameTimer !== null) {
        clearTimeout(this.usernameTimer);
      }
    });
  }

  selectCountry(countryId: string | null): void {
    this.countryId.set(countryId === '' ? null : countryId);
  }

  /** Back from the code step to the details: a new start issues a new request and a new code. */
  backToDetails(): void {
    this.stopTicking();
    this.verify.reset();
    this.resend.reset();
    this.step.set(1);
  }

  /** Debounced availability check while the username is typed (CLI-042 shape locally, CLI-101 by the server). */
  checkUsername(value: string): void {
    if (this.usernameTimer !== null) {
      clearTimeout(this.usernameTimer);
      this.usernameTimer = null;
    }

    const username = normaliseUsername(value);
    if (username.length === 0) {
      this.usernameState.set('idle');
      return;
    }

    if (!isUsernameShapeValid(username)) {
      this.usernameState.set('invalid');
      return;
    }

    const sequence = ++this.usernameSequence;
    this.usernameState.set('checking');
    this.usernameTimer = setTimeout(() => {
      this.usernameTimer = null;
      void this.api
        .checkUsername(username)
        .then((result: UsernameAvailability) => {
          if (sequence === this.usernameSequence) {
            this.usernameState.set(!result.valid ? 'invalid' : result.available ? 'available' : 'taken');
          }
        })
        .catch(() => {
          if (sequence === this.usernameSequence) {
            this.usernameState.set('error');
          }
        });
    }, USERNAME_CHECK_DELAY_MS);
  }

  private armResend(): void {
    this.now.set(Date.now());
    this.resendAt.set(Date.now() + RESEND_AFTER_SECONDS * 1000);
    if (this.tick === null) {
      this.tick = setInterval(() => {
        this.now.set(Date.now());
        if (this.resendIn() === 0 && this.tick !== null) {
          this.stopTicking();
        }
      }, 1000);
    }
  }

  private stopTicking(): void {
    if (this.tick !== null) {
      clearInterval(this.tick);
      this.tick = null;
    }
  }
}

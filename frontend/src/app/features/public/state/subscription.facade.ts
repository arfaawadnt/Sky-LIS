import { DestroyRef, Injectable, computed, inject, resource, signal } from '@angular/core';

import {
  type CheckSubscriptionRequest,
  type PublicCountry,
  SUBSCRIPTION_CODES,
  type StartSubscriptionRequest,
  type SubscriptionCheckResult,
  type SubscriptionCompletedResponse,
  type SubscriptionStarted,
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
 * What identifies the details a request was started with (LIS-CR-014 S3): every step-1 value but the language of the SMS
 * (switching the page language is not a change of details). Two requests with the same key need no new code.
 */
export function detailsKey(request: StartSubscriptionRequest): string {
  return JSON.stringify([
    request.labNameEn,
    request.labNameAr,
    request.countryId,
    request.governorateId,
    request.cityId ?? null,
    request.districtId ?? null,
    request.areaId ?? null,
    request.address,
    request.mobile,
    request.licenceNo,
    request.managerName,
    request.managerMobile,
    request.email,
  ]);
}

/**
 * State of the subscription wizard (01 §6.3): reference lists through `resource()`, the four calls through
 * `mutation()`, the step and the resend countdown in signals. Provided at the `subscribe` route, so leaving the page
 * forgets everything (nothing is kept in web storage).
 *
 * LIS-CR-014 (S3): the user may go back to step 1 from steps 2 and 3 (Back, or a done step of the rail) with every value kept.
 * Leaving step 1 again resumes the current request when no step-1 value changed — at step 3 when the mobile is already
 * verified, else at step 2 with the code already sent — and starts a new request (a new code) otherwise; the old request is
 * simply left to expire. A step-1 field refused at step 3 (SUB-011) sends the user back to step 1 and forces a new request.
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
  /** The current request's mobile has been verified (step 2 done). */
  readonly verified = signal(false);
  /** The key (`detailsKey`) of the details the current request was started with; `null` forces a new request. */
  readonly startedKey = signal<string | null>(null);
  /** Shown at step 1 after a step-1 field was refused at step 3: change it, then a new code is sent. */
  readonly returnedForDetails = signal(false);
  /** The rail opened the done step 2 of a verified request (a notice instead of the code form). */
  readonly reviewingVerified = computed(() => this.step() === 2 && this.verified());
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
      this.verified.set(false);
      this.returnedForDetails.set(false);
      this.verify.reset();
      this.resend.reset();
      this.complete.reset();
      this.usernameState.set('idle');
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
    then: () => {
      this.verified.set(true);
      this.step.set(3);
    },
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

  /**
   * Leaves step 1 (LIS-CR-014 S3): the same details as the current, live request resume it — step 3 when verified, step 2
   * otherwise — without a new code; anything else starts a new request. Resolves to `true` when the wizard moved on.
   */
  async continueFromDetails(request: StartSubscriptionRequest): Promise<boolean> {
    const key = detailsKey(request);
    if (this.resumes(key)) {
      this.start.reset();
      this.step.set(this.verified() ? 3 : 2);
      return true;
    }

    const started = await this.start.execute(request);
    if (started === null) {
      return false;
    }

    this.startedKey.set(key);
    return true;
  }

  /** Whether leaving step 1 with these details resumes the current request (no new code). */
  resumes(key: string): boolean {
    const started = this.started();
    return started !== null && this.startedKey() === key && Date.parse(started.expiresAtUtc) > Date.now();
  }

  /** Back to the details from steps 2 and 3, every value kept (Back button, or step 1 of the rail). */
  backToDetails(): void {
    if (this.step() === 4) {
      return;
    }

    this.verify.reset();
    this.resend.reset();
    this.complete.reset();
    this.step.set(1);
  }

  /** A done step of the rail opens: step 1 from steps 2 and 3, step 2 (the verified notice) from step 3; nothing from step 4. */
  canOpenStep(step: WizardStep): boolean {
    const current = this.step();
    return current !== 4 && step < current;
  }

  openStep(step: WizardStep): void {
    if (!this.canOpenStep(step)) {
      return;
    }

    if (step === 1) {
      this.backToDetails();
    } else {
      this.step.set(step);
    }
  }

  /** From the verified notice of step 2 on to the account step. */
  continueVerified(): void {
    if (this.verified()) {
      this.step.set(3);
    }
  }

  /**
   * A step-1 field was refused at step 3 (SUB-011: the mobile already belongs to an account): back to step 1 with the notice
   * "change it, then a new code is sent"; the next send always starts a new request, whose start checks the details again.
   */
  returnForDetails(): void {
    this.startedKey.set(null);
    this.returnedForDetails.set(true);
    this.backToDetails();
  }

  /** LIS-CR-014 S1: the mobiles of step 1 checked before the code is sent; no issues when the check cannot answer (start checks again). */
  async checkDetails(request: CheckSubscriptionRequest): Promise<SubscriptionCheckResult> {
    try {
      return await this.api.check(request);
    } catch {
      return { issues: [] };
    }
  }

  /** Debounced availability check while the username is typed (CLI-042 shape locally, SUB-013 by the server for the verified request). */
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

    const subscriptionId = this.started()?.subscriptionId;
    if (subscriptionId === undefined) {
      this.usernameState.set('idle');
      return;
    }

    const sequence = ++this.usernameSequence;
    this.usernameState.set('checking');
    this.usernameTimer = setTimeout(() => {
      this.usernameTimer = null;
      void this.api
        .usernameAvailable(subscriptionId, username)
        .then((result: SubscriptionCheckResult) => {
          if (sequence === this.usernameSequence) {
            this.usernameState.set(usernameStateOf(result));
          }
        })
        .catch(() => {
          if (sequence === this.usernameSequence) {
            this.usernameState.set('error');
          }
        });
    }, USERNAME_CHECK_DELAY_MS);
  }

  /** The username was refused by `complete` (SUB-013 / CLI-101): the line under the field says so. */
  markUsernameTaken(): void {
    this.usernameSequence++;
    this.usernameState.set('taken');
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

/** The state shown under the username for the answer of `username-available`. */
function usernameStateOf(result: SubscriptionCheckResult): UsernameState {
  const issue = result.issues.find((item) => item.field === 'username');
  if (issue === undefined) {
    return 'available';
  }

  if (issue.code === SUBSCRIPTION_CODES.usernameInUse || issue.code === SUBSCRIPTION_CODES.usernameTaken) {
    return 'taken';
  }

  return issue.code === SUBSCRIPTION_CODES.usernameInvalid ? 'invalid' : 'error';
}

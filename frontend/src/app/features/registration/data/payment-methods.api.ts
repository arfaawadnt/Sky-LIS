import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { BILLING_API, type PaymentMethodOption } from '../../../core/api/billing.contracts';
import { abortable } from './registration.api';

/**
 * The laboratory's active payment methods for the payment panel of the registration page (CP-30b, change request
 * 2026-10-19 P-C10): read through Billing's options endpoint with `billing.view`, the key the reception role already
 * holds to pay (BR-26). Tolerant like `myTreasuries`: a refused or failed read answers an empty list and the panel
 * falls back to the four kinds with the legacy reference rule.
 */
@Injectable({ providedIn: 'root' })
export class RegistrationPaymentMethodsApi {
  private readonly http = inject(HttpClient);

  options(signal?: AbortSignal): Promise<readonly PaymentMethodOption[]> {
    return firstValueFrom(this.http.get<readonly PaymentMethodOption[]>(BILLING_API.paymentMethodOptions), { defaultValue: [] as readonly PaymentMethodOption[] })
      .catch((): readonly PaymentMethodOption[] => [])
      .then(abortable(signal));
  }
}

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  type CompleteSubscriptionRequest,
  PUBLIC_SUBSCRIPTION_API,
  type PublicCountry,
  type PublicGovernorate,
  type StartSubscriptionRequest,
  type SubscriptionCompletedResponse,
  type SubscriptionStarted,
  type UsernameAvailability,
} from '../../../core/api/public-subscription.contracts';

/**
 * The anonymous subscription API of the platform module (CP-02), typed. The password of `complete` is a
 * `[CredentialField]`: the envelope interceptor seals it once `credential-fields.ts` lists the endpoint.
 */
@Injectable({ providedIn: 'root' })
export class PublicSubscriptionApi {
  private readonly http = inject(HttpClient);

  countries(): Promise<readonly PublicCountry[]> {
    return firstValueFrom(this.http.get<readonly PublicCountry[]>(PUBLIC_SUBSCRIPTION_API.countries), { defaultValue: [] });
  }

  governorates(countryId: string): Promise<readonly PublicGovernorate[]> {
    const params = new HttpParams({ fromObject: { countryId } });
    return firstValueFrom(this.http.get<readonly PublicGovernorate[]>(PUBLIC_SUBSCRIPTION_API.governorates, { params }), { defaultValue: [] });
  }

  start(request: StartSubscriptionRequest): Promise<SubscriptionStarted> {
    return firstValueFrom(this.http.post<SubscriptionStarted>(PUBLIC_SUBSCRIPTION_API.start, request));
  }

  resend(subscriptionId: string): Promise<SubscriptionStarted> {
    return firstValueFrom(this.http.post<SubscriptionStarted>(PUBLIC_SUBSCRIPTION_API.resend, { subscriptionId }));
  }

  verify(subscriptionId: string, code: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(PUBLIC_SUBSCRIPTION_API.verify, { subscriptionId, code }), { defaultValue: undefined }).then(() => undefined);
  }

  complete(request: CompleteSubscriptionRequest): Promise<SubscriptionCompletedResponse> {
    return firstValueFrom(this.http.post<SubscriptionCompletedResponse>(PUBLIC_SUBSCRIPTION_API.complete, request));
  }

  checkUsername(username: string): Promise<UsernameAvailability> {
    const params = new HttpParams({ fromObject: { username } });
    return firstValueFrom(this.http.get<UsernameAvailability>(PUBLIC_SUBSCRIPTION_API.checkUsername, { params }));
  }
}

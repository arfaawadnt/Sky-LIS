import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PLATFORM_API, type SubscriptionClientList, type SubscriptionFilter } from '../../../core/api/platform.contracts';

/** LIS-CR-012 (C-16): the SA-04 "Subscription" filter's list (`platform.view`, platform tenant only). */
@Injectable({ providedIn: 'root' })
export class SubscriptionClientsApi {
  private readonly http = inject(HttpClient);

  /** The clients of `state`; `days` (1–60) narrows `expiring`. Aborted with the resource that asked. */
  list(state: SubscriptionFilter, days: number | null, signal?: AbortSignal): Promise<SubscriptionClientList> {
    let params = new HttpParams().set('state', state);
    if (days !== null) {
      params = params.set('days', days);
    }

    const request = firstValueFrom(this.http.get<SubscriptionClientList>(PLATFORM_API.subscriptionClients, { params }));
    return signal ? abortable(request, signal) : request;
  }
}

function abortable<T>(request: Promise<T>, signal: AbortSignal): Promise<T> {
  if (signal.aborted) {
    return Promise.reject(signal.reason);
  }

  return new Promise<T>((resolve, reject) => {
    signal.addEventListener('abort', () => reject(signal.reason), { once: true });
    request.then(resolve, reject);
  });
}

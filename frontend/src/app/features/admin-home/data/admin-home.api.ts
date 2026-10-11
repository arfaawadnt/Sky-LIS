import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { type ClientCounts, PLATFORM_API, type PlatformDashboard } from '../../../core/api/platform.contracts';

/**
 * The client counts of the Super Admin home (UAT R2-05): read from the platform dashboard endpoint (SA-03) that the
 * dashboard page uses, so both pages show the same figures. Client counts do not depend on the period.
 */
@Injectable({ providedIn: 'root' })
export class AdminHomeApi {
  private readonly http = inject(HttpClient);

  async clientCounts(signal?: AbortSignal): Promise<ClientCounts> {
    const dashboard = await firstValueFrom(this.http.get<PlatformDashboard>(PLATFORM_API.dashboard, { params: { period: 'month' } }));
    signal?.throwIfAborted();
    return dashboard.clients;
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PUBLIC_LABORATORY_API, type PublicLaboratory } from '../../../core/api/public-laboratory.contracts';

/** The anonymous laboratory card of a sign-in address (LIS-CR-005); `404` when the address names no active laboratory. */
@Injectable({ providedIn: 'root' })
export class PublicLaboratoryApi {
  private readonly http = inject(HttpClient);

  card(slug: string): Promise<PublicLaboratory> {
    return firstValueFrom(this.http.get<PublicLaboratory>(PUBLIC_LABORATORY_API.card(slug)));
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AUTH_API, type CredentialChallengeResponse } from '../api/auth.contracts';
import { seal, sealFields } from './credential-envelope';

/**
 * Fetches a challenge from `/api/v1/auth/credential-challenge` and seals credentials with it (02 §3.3, §3.5).
 * No third-party cryptography: `crypto.subtle` only. The envelope interceptor is the normal caller; a feature
 * may call it directly for a credential that travels outside a request body.
 */
@Injectable({ providedIn: 'root' })
export class CredentialEnvelopeClient {
  private readonly http = inject(HttpClient);

  /** One challenge per request; never reused (the server consumes the nonce on first use). */
  challenge(): Promise<CredentialChallengeResponse> {
    return firstValueFrom(this.http.get<CredentialChallengeResponse>(AUTH_API.challenge));
  }

  /** Seals one value for `field` of the request `audience` (`"METHOD /path"`). */
  async seal(field: string, value: string, audience: string): Promise<string> {
    return seal(await this.challenge(), field, value, audience);
  }

  /** Seals every listed property of `body` under a single fresh challenge. */
  async sealFields(body: Readonly<Record<string, unknown>>, fields: readonly string[], audience: string): Promise<Record<string, unknown>> {
    return sealFields(body, fields, await this.challenge(), audience);
  }
}

import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { AdminSessionItem } from '../../../core/api/platform.contracts';
import { mutation } from '../../../core/state/mutation';
import { PlatformApi } from '../data/platform.api';

/** SA-08: live sessions of the platform administrators and their termination. */
@Injectable()
export class SessionsFacade {
  private readonly api = inject(PlatformApi);

  readonly search = signal<string | null>(null);

  readonly sessions = resource({
    params: () => this.search(),
    loader: ({ params, abortSignal }) => this.api.listSessions(params, abortSignal),
  });

  readonly rows = computed<readonly AdminSessionItem[]>(() => this.sessions.value() ?? []);

  readonly terminate = mutation((sessionId: string) => this.api.terminateSession(sessionId), { then: () => this.sessions.reload() });

  /** Re-reads the sessions when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.sessions.reload();
  }

  setSearch(search: string): void {
    this.search.set(search === '' ? null : search);
  }
}

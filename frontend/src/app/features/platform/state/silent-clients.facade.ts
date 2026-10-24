import { Injectable, computed, inject, resource, signal } from '@angular/core';

import { DEFAULT_SILENT_DAYS, type SilentClientItem } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { type DownloadedFile, PlatformApi } from '../data/platform.api';

/**
 * LIS-CR-007 A-01: the clients with no activity for a window (SA-04 "Silent" filter). One small list, never paged; the
 * server orders it longest silence first. The export is the server's CSV in the reader's language.
 */
@Injectable()
export class SilentClientsFacade {
  private readonly api = inject(PlatformApi);
  private readonly i18n = inject(I18nService);

  /** The window; nothing loads until the owner sets it (the table sets its input before the first read). */
  readonly days = signal<number | null>(null);

  readonly list = resource({
    params: () => this.days() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.silentClients(params, abortSignal),
  });

  readonly rows = computed<readonly SilentClientItem[]>(() => (this.list.hasValue() ? this.list.value().items : []));

  readonly trial = computed(() => (this.list.hasValue() ? this.list.value().trial : 0));

  readonly export = mutation<void, DownloadedFile>(() => this.api.exportSilentClients(this.days() ?? DEFAULT_SILENT_DAYS, this.i18n.lang()));

  setDays(days: number): void {
    this.days.set(days);
  }

  refresh(): void {
    this.list.reload();
  }
}

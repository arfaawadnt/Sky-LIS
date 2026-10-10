import { Injectable, inject, resource, signal } from '@angular/core';

import type { AuditActorItem } from '../../../core/api/account.contracts';
import type { AuditEntryFilter, AuditEntryItem, ChainVerificationResult } from '../../../core/api/audit.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { AccountApi, type DownloadedFile } from '../data/account.api';

/** The filter as the page edits it: local days (`yyyy-MM-dd`) rather than instants. */
export interface AuditFilterInput {
  readonly fromDay: string;
  readonly toDay: string;
  readonly userId: string;
  readonly entityType: string;
  readonly entityId: string;
}

export const EMPTY_AUDIT_FILTER: AuditFilterInput = { fromDay: '', toDay: '', userId: '', entityType: '', entityId: '' };

/** A local day's first instant, or the last one with `endOfDay`, as ISO-8601 UTC; `null` for an empty or invalid day. */
export function dayToInstant(day: string, endOfDay: boolean): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (!match) {
    return null;
  }

  const date = endOfDay ? new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]), 23, 59, 59, 999) : new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function blank(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

/**
 * CP-68 / SA-09 state: the unified audit log of the current tenant (field changes and trail events, newest first),
 * the members for the user filter, the chain verification and the CSV export (`audit.export`).
 */
@Injectable()
export class AuditLogFacade {
  private readonly api = inject(AccountApi);
  private readonly i18n = inject(I18nService);

  readonly input = signal<AuditFilterInput>(EMPTY_AUDIT_FILTER);
  readonly filter = signal<AuditEntryFilter>({ from: null, to: null, userId: null, entityType: null, entityId: null, page: 1, pageSize: 50 });
  readonly selected = signal<AuditEntryItem | null>(null);
  readonly verification = signal<ChainVerificationResult | null>(null);

  readonly entries = resource({ params: () => this.filter(), loader: ({ params }) => this.api.auditEntries(params) });

  readonly actors = resource({ loader: (): Promise<readonly AuditActorItem[]> => this.api.auditActors().catch(() => []) });

  readonly verify = mutation(() => this.api.verifyChain(), { then: (result) => this.verification.set(result) });

  readonly export = mutation((): Promise<DownloadedFile> => {
    const { from, to, userId, entityType, entityId } = this.filter();
    return this.api.exportAudit({ from, to, userId, entityType, entityId }, this.i18n.lang());
  });

  /** Applies the edited filter (back to the first page). */
  apply(input: AuditFilterInput): void {
    this.input.set(input);
    this.filter.update((current) => ({
      ...current,
      from: dayToInstant(input.fromDay, false),
      to: dayToInstant(input.toDay, true),
      userId: blank(input.userId),
      entityType: blank(input.entityType),
      entityId: blank(input.entityId),
      page: 1,
    }));
  }

  /** "Show the history of this record" from the detail drawer. */
  showRecord(entityType: string, entityId: string): void {
    this.apply({ ...EMPTY_AUDIT_FILTER, entityType, entityId });
    this.selected.set(null);
  }

  reset(): void {
    this.apply(EMPTY_AUDIT_FILTER);
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }
}

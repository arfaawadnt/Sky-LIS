import { Injectable, computed, inject, resource, signal } from '@angular/core';

import { BILLING_PERMISSIONS } from '../../../core/api/billing.contracts';
import type { CancelLinesRequest, VisitDetail, VisitListItem } from '../../../core/api/registration.contracts';
import { RESULTS_PERMISSIONS, RESULT_STATUSES, type ResultStatus } from '../../../core/api/results.contracts';
import { SAMPLES_PERMISSIONS } from '../../../core/api/samples.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { RegistrationApi } from '../data/registration.api';

/** What the page opens: a visit by id (`?visitId=`) or by its accession (`?accession=`). */
export type VisitKey = { readonly visitId: string } | { readonly accession: string };

/** The answer of the search strip: one visit to open, a list to pick from, nothing, or a failed read. */
export type SearchAnswer =
  | { readonly kind: 'opened'; readonly visit: VisitListItem }
  | { readonly kind: 'list'; readonly count: number }
  | { readonly kind: 'none' }
  | { readonly kind: 'failed' };

/** The `detail` resource's error when no visit carries the accession asked for. */
export const ACCESSION_NOT_FOUND = 'ACCESSION_NOT_FOUND';

/**
 * State of Accession details (CP-10b, change request 2026-10-19 LIS-CR-003 P-C9; 01 §6.3): the open visit (by id or
 * accession) through `resource()`, the reads the cards add — samples, Billing's account, the result case — each asked
 * only with the permission its module checks (the server still decides) and read tolerantly (a refusal is an empty
 * card, not a broken page), the search strip, and the two writes (cancel lines, collect a sample) through `mutation()`.
 * Provided at the route: the page calls `refresh()` on every visit so a previous session's answer never stays on screen.
 */
@Injectable()
export class AccessionFacade {
  private readonly api = inject(RegistrationApi);
  private readonly auth = inject(AuthStore);

  readonly key = signal<VisitKey | null>(null);

  readonly detail = resource({
    params: () => this.key() ?? undefined,
    loader: ({ params, abortSignal }) => this.load(params, abortSignal),
  });

  /** The id of the open visit, once it is loaded: the key of the dependent reads. */
  readonly visitId = computed(() => (this.detail.hasValue() ? this.detail.value().id : undefined));

  readonly samples = resource({
    params: () => (this.auth.hasPermission(SAMPLES_PERMISSIONS.view) ? this.visitId() : undefined),
    loader: ({ params, abortSignal }) => this.api.samples(params, abortSignal),
  });

  readonly account = resource({
    params: () => (this.auth.hasPermission(BILLING_PERMISSIONS.view) ? this.visitId() : undefined),
    loader: ({ params, abortSignal }) => this.api.visitAccount(params, abortSignal),
  });

  readonly results = resource({
    params: () => (this.auth.hasPermission(RESULTS_PERMISSIONS.view) ? this.visitId() : undefined),
    loader: ({ params, abortSignal }) => this.api.resultCase(params, abortSignal),
  });

  /**
   * The result status of every ordered line, by line id: the earliest stage among the line's live results (a profile is
   * "Complete" only when every test is), a package the earliest of its members; lines without a result are absent.
   */
  readonly lineResults = computed<ReadonlyMap<string, ResultStatus>>(() => {
    const view = this.results.hasValue() ? this.results.value() : null;
    const detail = this.detail.hasValue() ? this.detail.value() : null;
    const byLine = new Map<string, ResultStatus>();
    if (view === null || detail === null) {
      return byLine;
    }

    for (const unit of view.units) {
      for (const row of unit.results) {
        byLine.set(row.lineId, earliest(byLine.get(row.lineId), row.status));
      }
    }

    for (const line of detail.lines) {
      if (line.kind === 'Package' && !byLine.has(line.id)) {
        for (const member of detail.lines) {
          const status = member.parentLineId === line.id ? byLine.get(member.id) : undefined;
          if (status !== undefined) {
            byLine.set(line.id, earliest(byLine.get(line.id), status));
          }
        }
      }
    }

    return byLine;
  });

  /**
   * The lines the server will not let reception cancel (REG-106): a line — or a package through any member — with a
   * live Verified or Printed result. The page hides *Cancel* for them; the server still decides.
   */
  readonly lockedLines = computed<ReadonlySet<string>>(() => {
    const view = this.results.hasValue() ? this.results.value() : null;
    const detail = this.detail.hasValue() ? this.detail.value() : null;
    const locked = new Set<string>();
    if (view === null || detail === null) {
      return locked;
    }

    for (const unit of view.units) {
      for (const row of unit.results) {
        if (row.status === 'Verified' || row.status === 'Printed') {
          locked.add(row.lineId);
        }
      }
    }

    for (const member of detail.lines) {
      if (member.parentLineId !== null && locked.has(member.id)) {
        locked.add(member.parentLineId);
      }
    }

    return locked;
  });

  // ---- search strip ----------------------------------------------------------------------------------------------------

  readonly searchText = signal('');
  readonly searching = signal(false);
  readonly matches = signal<readonly VisitListItem[]>([]);
  /** The compact list of matches is shown (several visits matched the text). */
  readonly listOpen = signal(false);

  // ---- writes ------------------------------------------------------------------------------------------------------------

  readonly cancel = mutation((input: { visitId: string; request: CancelLinesRequest }) => this.api.cancel(input.visitId, input.request), { then: () => this.reloadVisit() });

  readonly collect = mutation((sampleNumber: string) => this.api.collectSample(sampleNumber), {
    then: () => {
      this.samples.reload();
      this.detail.reload();
      this.results.reload();
    },
  });

  open(key: VisitKey | null): void {
    this.key.set(key);
  }

  /**
   * Runs the search text (Enter): exactly one match — or, among several, the one whose accession is exactly the text —
   * is the answer to open; several open the list to pick from; nothing says so.
   */
  async search(text: string): Promise<SearchAnswer> {
    const trimmed = text.trim();
    this.searchText.set(trimmed);
    if (trimmed === '') {
      this.closeList();
      return { kind: 'none' };
    }

    this.searching.set(true);
    try {
      const found = await this.api.searchVisits(trimmed);
      const single = pickSingle(found.items, trimmed);
      if (single !== null) {
        this.closeList();
        return { kind: 'opened', visit: single };
      }

      this.matches.set(found.items);
      this.listOpen.set(found.items.length > 0);
      return found.items.length === 0 ? { kind: 'none' } : { kind: 'list', count: found.totalCount };
    } catch {
      this.closeList();
      return { kind: 'failed' };
    } finally {
      this.searching.set(false);
    }
  }

  closeList(): void {
    this.listOpen.set(false);
    this.matches.set([]);
  }

  /** The visit of an exact accession (previous / next), or null when there is none. */
  findByAccession(accession: string): Promise<VisitListItem | null> {
    return this.api.findByAccession(accession);
  }

  /** Re-reads everything when the page opens again: the facade outlives the page (route provider). A load in flight is left alone. */
  refresh(): void {
    this.cancel.reset();
    this.collect.reset();
    this.closeList();
    this.reloadVisit();
  }

  private reloadVisit(): void {
    this.detail.reload();
    this.samples.reload();
    this.account.reload();
    this.results.reload();
  }

  private async load(key: VisitKey, signal: AbortSignal): Promise<VisitDetail> {
    if ('visitId' in key) {
      return this.api.get(key.visitId, signal);
    }

    const found = await this.api.findByAccession(key.accession, signal);
    if (found === null) {
      throw new Error(ACCESSION_NOT_FOUND);
    }

    return this.api.get(found.id, signal);
  }
}

/** The one visit a search text designates: the only match, or the match whose accession is exactly the text. */
export function pickSingle(items: readonly VisitListItem[], text: string): VisitListItem | null {
  if (items.length === 1) {
    return items[0] ?? null;
  }

  const upper = text.trim().toUpperCase();
  return items.find((item) => item.accession.toUpperCase() === upper) ?? null;
}

/** The earlier of two result stages (Cancelled counts last, so a live result always wins). */
function earliest(current: ResultStatus | undefined, candidate: ResultStatus): ResultStatus {
  if (current === undefined) {
    return candidate;
  }

  return RESULT_STATUSES.indexOf(candidate) < RESULT_STATUSES.indexOf(current) ? candidate : current;
}

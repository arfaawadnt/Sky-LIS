import { Injectable, computed, inject, resource, signal } from '@angular/core';

import { ProblemError } from '../../../core/api/problem-details';
import type {
  BulkResultOutcome,
  CultureEntryDto,
  FormattedParagraphDto,
  LaboratoryReportDocument,
  ReportUnitView,
  ResultCaseView,
  ResultRefusal,
  ResultRowView,
  ResultValueItem,
  ResultVisitQuery,
} from '../../../core/api/results.contracts';
import { mutation } from '../../../core/state/mutation';
import { type AmendInput, ResultsApi } from '../data/results.api';
import { initialDraft, isSigned, isTyped, toValueItem } from '../ui/result-format';
import type { HistoryTab } from '../ui/result-history.component';

/** What opens a case: a visit picked from the query panel, or an accession from the route or another page. */
export type CaseKey = { readonly visitId: string } | { readonly accession: string };

export type { HistoryTab };

/**
 * CP-19 Results by accession (01 §6.3): the query panel and its matching visits, the open case with its report units,
 * the drafts typed in the grid (saved together with Ctrl+S), the unit actions (save, review, verify, undo, preview,
 * print), the row actions (amend, delta acknowledgement, comment, note, culture and text entry) and the history drawer.
 */
@Injectable()
export class ResultsCaseFacade {
  private readonly api = inject(ResultsApi);

  /** The last query run from the panel; null until the first run. */
  readonly query = signal<ResultVisitQuery | null>(null);

  readonly visits = resource({
    params: () => this.query() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.searchVisits(params, abortSignal),
  });

  readonly key = signal<CaseKey | null>(null);

  readonly caseView = resource({
    params: () => this.key() ?? undefined,
    loader: ({ params, abortSignal }) => ('visitId' in params ? this.api.caseByVisit(params.visitId, abortSignal) : this.api.caseByAccession(params.accession, abortSignal)),
  });

  readonly current = computed<ResultCaseView | null>(() => (this.caseView.hasValue() ? this.caseView.value() : null));

  /** The unit tab the user chose; falls back to the first unit. */
  readonly activeUnitKey = signal<string | null>(null);

  readonly activeUnit = computed<ReportUnitView | null>(() => {
    const units = this.current()?.units ?? [];
    return units.find((unit) => unit.key === this.activeUnitKey()) ?? units[0] ?? null;
  });

  /** Grid drafts by result id: only rows the user changed. */
  readonly drafts = signal<Readonly<Record<string, string>>>({});

  /** The last refusals of a save, by result id (shown on the rows). */
  readonly refusals = signal<Readonly<Record<string, string>>>({});

  readonly dirtyCount = computed(() => Object.keys(this.drafts()).length);

  readonly historyRow = signal<ResultRowView | null>(null);

  readonly historyTab = signal<HistoryTab>('log');

  readonly log = resource({
    params: () => this.historyRow()?.id,
    loader: ({ params, abortSignal }) => this.api.log(params, abortSignal),
  });

  readonly previous = resource({
    params: () => this.historyRow()?.id,
    loader: ({ params, abortSignal }) => this.api.previous(params, abortSignal),
  });

  /** Loaded the first time the culture dialog opens. */
  readonly microbiologyWanted = signal(false);

  readonly microbiology = resource({
    params: () => (this.microbiologyWanted() ? true : undefined),
    loader: ({ abortSignal }) => this.api.microbiology(abortSignal),
  });

  readonly commentSearch = signal<{ readonly search: string; readonly groupId: string | null } | null>(null);

  readonly commentTemplates = resource({
    params: () => this.commentSearch() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.commentTemplates(params.search.trim().length === 0 ? null : params.search, params.groupId, abortSignal),
  });

  readonly save = mutation((values: readonly ResultValueItem[]) => this.api.saveValues(values), { then: (outcome) => this.afterSave(outcome) });

  readonly review = mutation((ids: readonly string[]) => this.api.reviewMany(ids), { then: (outcome) => this.afterBulk(outcome) });

  readonly verify = mutation((ids: readonly string[]) => this.api.verifyMany(ids), { then: (outcome) => this.afterBulk(outcome) });

  /** T5 per result (the API undoes one at a time): a refusal of one row does not stop the others; the outcome lists both. */
  readonly undo = mutation(
    async (input: { readonly ids: readonly string[]; readonly reason: string }): Promise<BulkResultOutcome> => {
      const changed: string[] = [];
      const refused: ResultRefusal[] = [];
      for (const id of input.ids) {
        try {
          await this.api.undo(id, input.reason);
          changed.push(id);
        } catch (error: unknown) {
          if (error instanceof ProblemError) {
            refused.push({ resultId: id, code: error.code, message: error.problem.title });
          } else {
            throw error;
          }
        }
      }

      return { changed, refused };
    },
    { then: (outcome) => this.afterBulk(outcome) },
  );

  readonly amend = mutation((input: { readonly id: string; readonly value: AmendInput; readonly reason: string }) => this.api.amend(input.id, input.value, input.reason), { then: () => this.reload() });

  readonly acknowledge = mutation((input: { readonly id: string; readonly comment: string | null }) => this.api.acknowledgeDelta(input.id, input.comment), { then: () => this.reload() });

  /** T7 for every listed result, then T4: the "acknowledge and verify" of the delta dialog (BR-11). */
  readonly acknowledgeAndVerify = mutation(
    async (input: { readonly deltaIds: readonly string[]; readonly verifyIds: readonly string[]; readonly comment: string | null }): Promise<BulkResultOutcome> => {
      for (const id of input.deltaIds) {
        await this.api.acknowledgeDelta(id, input.comment);
      }

      return this.api.verifyMany(input.verifyIds);
    },
    { then: (outcome) => this.afterBulk(outcome) },
  );

  readonly comment = mutation((input: { readonly id: string; readonly text: string | null }) => this.api.setComment(input.id, input.text), { then: () => this.reload() });

  readonly note = mutation((input: { readonly id: string; readonly text: string | null }) => this.api.setNote(input.id, input.text), { then: () => this.reload() });

  /** Culture and formatted text: entered like any value, amended when the result is signed (reason required). */
  readonly report = mutation(
    async (input: { readonly row: ResultRowView; readonly paragraphs: readonly FormattedParagraphDto[] | null; readonly culture: CultureEntryDto | null; readonly reason: string | null }): Promise<BulkResultOutcome | null> => {
      if (isSigned(input.row.status)) {
        await this.api.amend(input.row.id, { numericValue: null, textValue: null, paragraphs: input.paragraphs, culture: input.culture }, input.reason ?? '');
        return null;
      }

      return this.api.saveValues([{ resultId: input.row.id, numericValue: null, textValue: null, paragraphs: input.paragraphs, culture: input.culture }]);
    },
    { then: () => this.reload() },
  );

  readonly preview = mutation((reportUnitKey: string): Promise<LaboratoryReportDocument> => this.api.report(reportUnitKey, true));

  /** Marks the unit printed (T8), then fetches the official copy to render. */
  readonly print = mutation(async (reportUnitKey: string): Promise<LaboratoryReportDocument> => {
    await this.api.print(reportUnitKey);
    return this.api.report(reportUnitKey, false);
  }, { then: () => this.reload() });

  run(query: ResultVisitQuery): void {
    this.query.set(query);
  }

  open(key: CaseKey): void {
    this.drafts.set({});
    this.refusals.set({});
    this.activeUnitKey.set(null);
    this.key.set(key);
  }

  close(): void {
    this.key.set(null);
    this.drafts.set({});
    this.refusals.set({});
  }

  reload(): void {
    this.caseView.reload();
    if (this.historyRow()) {
      this.log.reload();
      this.previous.reload();
    }
  }

  setDraft(row: ResultRowView, value: string): void {
    this.drafts.update((drafts) => {
      const next = { ...drafts };
      if (value.trim() === initialDraft(row).trim()) {
        delete next[row.id];
      } else {
        next[row.id] = value;
      }

      return next;
    });
  }

  /** The typed drafts of a unit as save items (rows the user did not change are not sent). */
  draftItems(unit: ReportUnitView): readonly ResultValueItem[] {
    const drafts = this.drafts();
    return unit.results.filter((row) => isTyped(row) && drafts[row.id] !== undefined).map((row) => toValueItem(row, drafts[row.id] ?? ''));
  }

  /** Complete results of the unit (Review); Complete or Reviewed results with a value (Verify). */
  reviewable(unit: ReportUnitView): readonly string[] {
    return unit.results.filter((row) => row.status === 'Complete').map((row) => row.id);
  }

  verifiable(unit: ReportUnitView): readonly string[] {
    return unit.results.filter((row) => row.status === 'Complete' || row.status === 'Reviewed').map((row) => row.id);
  }

  /** Reviewed rows need the review right, Verified rows the verify right (T5); Printed rows are amended, not undone. */
  undoable(unit: ReportUnitView): readonly string[] {
    return unit.results
      .filter((row) => (row.status === 'Reviewed' && unit.rights.canReview) || (row.status === 'Verified' && unit.rights.canVerify))
      .map((row) => row.id);
  }

  /** Verifiable rows whose delta flag is not acknowledged yet (the server refuses them with RES-015). */
  unacknowledgedDeltas(unit: ReportUnitView): readonly ResultRowView[] {
    return unit.results.filter((row) => (row.status === 'Complete' || row.status === 'Reviewed') && row.delta?.flag === true && !row.delta.acknowledged);
  }

  /** The grid draft of a row, or its stored text. */
  draftOf(row: ResultRowView): string {
    return this.drafts()[row.id] ?? initialDraft(row);
  }

  openHistory(row: ResultRowView, tab: HistoryTab): void {
    this.historyTab.set(tab);
    this.historyRow.set(row);
  }

  closeHistory(): void {
    this.historyRow.set(null);
  }

  private afterSave(outcome: BulkResultOutcome): void {
    const refused = new Set(outcome.refused.map((refusal) => refusal.resultId));
    this.drafts.update((drafts) => Object.fromEntries(Object.entries(drafts).filter(([id]) => refused.has(id))));
    this.afterBulk(outcome);
  }

  /** A bulk transition shows its refusals on the rows (code per result) and reloads the case. */
  private afterBulk(outcome: BulkResultOutcome): void {
    this.refusals.set(Object.fromEntries(outcome.refused.map((refusal) => [refusal.resultId, refusal.code])));
    this.reload();
  }
}

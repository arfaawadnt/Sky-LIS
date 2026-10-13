import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import {
  type BulkResultOutcome,
  type LaboratoryReportDocument,
  RESULT_FILTER_STATUSES,
  RESULTS_PERMISSIONS,
  type ReportUnitView,
  type ResultCaseView,
  type ResultRowView,
  type ResultStatus,
  type ResultVisitListItem,
  type ResultVisitQuery,
  SAMPLE_FILTER_STATUSES,
  type SampleStatusName,
} from '../../../core/api/results.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import type { DocumentTexts } from '../../../shared/print/document-texts';
import { type LoadedTemplateImages, ReportTemplateImageLoader } from '../../../shared/print/report-template-images';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ResultsCaseFacade } from '../state/results-case.facade';
import { ResultsPrinter } from '../state/results-printer';
import { type AmendRequest, AmendDialogComponent } from '../ui/amend-dialog.component';
import { CommentDialogComponent } from '../ui/comment-dialog.component';
import { type CultureRequest, CultureDialogComponent } from '../ui/culture-dialog.component';
import { ReportDocumentComponent } from '../ui/report.document';
import { ageText, formatDateTime, localDayStart, pickName, screenNumber } from '../ui/result-format';
import { type DraftChange, ResultGridComponent } from '../ui/result-grid.component';
import { ResultHistoryComponent } from '../ui/result-history.component';
import { ResultTextDialogComponent } from '../ui/text-dialog.component';
import { type TextReportRequest, TextReportDialogComponent } from '../ui/text-report-dialog.component';

/** The server's refusal of a verification whose delta flag is not acknowledged (BR-11; `ResultsCodes.DeltaNotAcknowledged`). */
const DELTA_NOT_ACKNOWLEDGED = 'RES-015';

/**
 * CP-19 Results by accession (WF-06 steps 2–5; LDM §5.2–§5.7): the query panel (accession, patient number, name with
 * `%`, test and sample status, analyser, not printed / not verified / delta boxes, registration window; F8 runs it),
 * the matching visits, and the open case: patient and visit header, one tab per report unit with its samples, the
 * results grid (entry by test type, flags, panic, delta acknowledgement), Save (Ctrl+S), Review, Verify (with the
 * delta dialog, BR-11), Undo with a reason, Amend with a reason (special right), comment, internal note, history and
 * previous results, Preview and Print of the laboratory report (BR-09). Buttons follow the user's permissions and
 * the unit's result rights; the server decides and its refusals show on the rows.
 */
@Component({
  selector: 'app-results-by-accession-page',
  imports: [
    ReactiveFormsModule,
    SaveShortcutDirective,
    PageHeaderComponent,
    SectionCardComponent,
    TabsComponent,
    BadgeComponent,
    IconComponent,
    EmptyStateComponent,
    PaginatorComponent,
    DrawerComponent,
    DialogComponent,
    ResultGridComponent,
    AmendDialogComponent,
    ResultTextDialogComponent,
    CultureDialogComponent,
    TextReportDialogComponent,
    CommentDialogComponent,
    ResultHistoryComponent,
    ReportDocumentComponent,
  ],
  templateUrl: './results-by-accession.page.html',
  styleUrl: './results-by-accession.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.f8)': 'onRunKey($event)' },
})
export class ResultsByAccessionPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ResultsCaseFacade);
  private readonly permissions = inject(PermissionsService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly printer = inject(ResultsPrinter);
  private readonly templateImages = inject(ReportTemplateImageLoader);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** The `?accession=` query parameter (component input binding): other pages open a visit's results with it. */
  readonly accession = input<string>();

  protected readonly statuses = RESULT_FILTER_STATUSES;
  protected readonly sampleStatuses = SAMPLE_FILTER_STATUSES;

  protected readonly form = new FormGroup({
    accession: new FormControl('', { nonNullable: true }),
    patientNumber: new FormControl('', { nonNullable: true }),
    patientName: new FormControl('', { nonNullable: true }),
    status: new FormControl('', { nonNullable: true }),
    sampleStatus: new FormControl('', { nonNullable: true }),
    analyserId: new FormControl('', { nonNullable: true }),
    notPrinted: new FormControl(false, { nonNullable: true }),
    notVerified: new FormControl(false, { nonNullable: true }),
    delta: new FormControl(false, { nonNullable: true }),
    fromDay: new FormControl('', { nonNullable: true }),
    toDay: new FormControl('', { nonNullable: true }),
    branchOnly: new FormControl(true, { nonNullable: true }),
  });

  /** The matching-visits panel folds away once a case is open (prototype "hide filters"). */
  protected readonly listCollapsed = signal(false);

  protected readonly amendRow = signal<ResultRowView | null>(null);
  protected readonly amendOpen = signal(false);
  protected readonly noteRow = signal<ResultRowView | null>(null);
  protected readonly noteOpen = signal(false);
  protected readonly deltaRow = signal<ResultRowView | null>(null);
  protected readonly deltaOpen = signal(false);
  protected readonly cultureRow = signal<ResultRowView | null>(null);
  protected readonly cultureOpen = signal(false);
  protected readonly textRow = signal<ResultRowView | null>(null);
  protected readonly textOpen = signal(false);
  protected readonly commentRow = signal<ResultRowView | null>(null);
  protected readonly commentOpen = signal(false);
  protected readonly previewDoc = signal<LaboratoryReportDocument | null>(null);
  protected readonly previewOpen = signal(false);
  /** The logo and signature of the previewed report's template (validation F-02); released on the next preview and on leaving. */
  protected readonly previewImages = signal<LoadedTemplateImages | null>(null);
  /** The previewed report's captions in the report's own language (validation v0.5.0 F-12). */
  protected readonly previewTexts = signal<DocumentTexts | null>(null);
  protected readonly historyOpen = signal(false);

  protected readonly visits = computed<readonly ResultVisitListItem[]>(() => (this.facade.visits.hasValue() ? this.facade.visits.value().items : []));
  protected readonly visitsTotal = computed(() => (this.facade.visits.hasValue() ? this.facade.visits.value().totalCount : 0));
  protected readonly current = this.facade.current;
  protected readonly unit = this.facade.activeUnit;

  protected readonly caseProblem = computed(() => {
    const error = this.facade.caseView.error();
    if (!error) {
      return null;
    }

    return problemMessage(this.i18n, 'results', error instanceof ProblemError ? error.problem : { code: 'INTERNAL-500' });
  });

  protected readonly tabs = computed<readonly TabItem[]>(() =>
    (this.current()?.units ?? []).map((unit) => ({ id: unit.key, label: this.unitName(unit), badge: this.statusLabel(unit.status) })),
  );

  protected readonly busy = computed(
    () =>
      this.facade.save.pending() ||
      this.facade.review.pending() ||
      this.facade.verify.pending() ||
      this.facade.acknowledgeAndVerify.pending() ||
      this.facade.undo.pending() ||
      this.facade.print.pending() ||
      this.facade.preview.pending(),
  );

  constructor() {
    void this.i18n.load('results');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)
    inject(DestroyRef).onDestroy(() => this.previewImages()?.release());
    effect(() => {
      const accession = this.accession()?.trim().toUpperCase() ?? '';
      untracked(() => {
        if (accession.length === 0) {
          return;
        }

        const key = this.facade.key();
        if (key && 'accession' in key && key.accession === accession) {
          return;
        }

        this.form.patchValue({ accession });
        this.facade.open({ accession });
        this.listCollapsed.set(true);
      });
    });
    effect(() => {
      this.historyOpen.set(this.facade.historyRow() !== null);
    });
  }

  // ---- query panel -------------------------------------------------------------------------------------------------

  protected runQuery(page: PageRequest = { page: 1, pageSize: 40 }): void {
    const value = this.form.getRawValue();
    const text = (input: string): string | null => (input.trim().length === 0 ? null : input.trim());
    const query: ResultVisitQuery = {
      accession: text(value.accession)?.toUpperCase() ?? null,
      patientNumber: text(value.patientNumber),
      patientName: text(value.patientName),
      status: this.statuses.find((status) => status === value.status) ?? null,
      sampleStatus: this.sampleStatuses.find((status) => status === value.sampleStatus) ?? null,
      analyserId: text(value.analyserId),
      notPrinted: value.notPrinted,
      notVerified: value.notVerified,
      delta: value.delta,
      from: localDayStart(text(value.fromDay)),
      to: localDayStart(text(value.toDay), 1),
      branchOnly: value.branchOnly,
      page: page.page,
      pageSize: page.pageSize,
    };
    this.facade.run(query);
    this.listCollapsed.set(false);
  }

  protected onRunKey(event: Event): void {
    event.preventDefault();
    this.runQuery();
  }

  protected newQuery(): void {
    this.form.reset();
    this.facade.query.set(null);
    this.facade.close();
    this.listCollapsed.set(false);
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
  }

  protected select(item: ResultVisitListItem): void {
    this.facade.open({ accession: item.accession });
    this.listCollapsed.set(true);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { accession: item.accession }, replaceUrl: true });
  }

  protected onVisitsPage(page: PageRequest): void {
    this.runQuery(page);
  }

  // ---- unit actions ------------------------------------------------------------------------------------------------

  protected canEnter(unit: ReportUnitView): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.enter) && unit.rights.canEdit;
  }

  /** Internal notes (T11) need results.create; the report comment follows canEnter (UAT A-02). */
  protected canAnnotate(): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.enter);
  }

  protected canReview(unit: ReportUnitView): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.approve) && unit.rights.canReview;
  }

  protected canVerify(unit: ReportUnitView): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.approve) && unit.rights.canVerify;
  }

  protected canAcknowledge(unit: ReportUnitView): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.approve) && (unit.rights.canReview || unit.rights.canVerify);
  }

  protected canUndo(unit: ReportUnitView): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.undo) && (unit.rights.canReview || unit.rights.canVerify);
  }

  protected canAmend(unit: ReportUnitView): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.amend) && unit.rights.canEdit && unit.rights.canModifyVerified;
  }

  protected canPrint(unit: ReportUnitView): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.print) && unit.rights.canPrint;
  }

  protected canPreview(): boolean {
    return this.permissions.can(RESULTS_PERMISSIONS.view);
  }

  /** Ctrl+S on the case saves the active unit's drafts. */
  protected saveActive(): void {
    const unit = this.unit();
    if (unit) {
      void this.save(unit);
    }
  }

  protected async save(unit: ReportUnitView): Promise<void> {
    const items = this.facade.draftItems(unit);
    if (items.length === 0) {
      this.toast.info(this.i18n.t('results.case.nothingToSave'));
      return;
    }

    this.report(await this.facade.save.execute(items), 'saved', this.facade.save.problem());
  }

  protected async review(unit: ReportUnitView): Promise<void> {
    const ids = this.facade.reviewable(unit);
    if (ids.length === 0) {
      this.toast.warn(this.i18n.t('results.case.nothingToReview'));
      return;
    }

    this.report(await this.facade.review.execute(ids), 'reviewed', this.facade.review.problem());
  }

  protected async verify(unit: ReportUnitView): Promise<void> {
    const ids = this.facade.verifiable(unit);
    if (ids.length === 0) {
      this.toast.warn(this.i18n.t('results.case.nothingToVerify'));
      return;
    }

    const deltas = this.facade.unacknowledgedDeltas(unit);
    if (deltas.length === 0) {
      const outcome = await this.facade.verify.execute(ids);
      const deltaRefused = new Set(outcome?.refused.filter((refusal) => refusal.code === DELTA_NOT_ACKNOWLEDGED).map((refusal) => refusal.resultId) ?? []);
      if (outcome === null || deltaRefused.size === 0) {
        this.report(outcome, 'verified', this.facade.verify.problem());
        return;
      }

      // A-02: the server re-checked the delta at verification (an older result was verified after this one was reviewed)
      // and refused with RES-015. The click is not wasted: the case is re-read and the delta dialog opens with the
      // refreshed flags, so "Acknowledge and verify" signs the refused rows.
      this.report({ changed: outcome.changed, refused: outcome.refused.filter((refusal) => !deltaRefused.has(refusal.resultId)) }, 'verified', null);
      const refreshed = (await this.facade.whenCaseSettled())?.units.find((candidate) => candidate.key === unit.key) ?? null;
      const flagged = refreshed === null ? [] : this.facade.unacknowledgedDeltas(refreshed).filter((row) => deltaRefused.has(row.id));
      if (flagged.length === 0) {
        this.toast.warn(this.countText('results.case.refusedCount', deltaRefused.size));
        return;
      }

      await this.acknowledgeAndVerify(flagged, flagged.map((row) => row.id));
      return;
    }

    await this.acknowledgeAndVerify(deltas, ids);
  }

  /** The delta dialog (BR-11): lists the flags; "Acknowledge and verify" acknowledges them and verifies `verifyIds`. */
  private async acknowledgeAndVerify(deltas: readonly ResultRowView[], verifyIds: readonly string[]): Promise<void> {
    const percent = (value: number | null | undefined): string => (value === null || value === undefined ? '—' : this.i18n.tf('results.worklist.percent', { value: this.num(value) }));
    const lines = deltas.map((row) => `${this.testName(row)}: ${row.textValue ?? '—'} (Δ ${percent(row.delta?.percent)}, ${this.i18n.t('results.case.previous')} ${row.delta?.previousValue ?? '—'})`).join('\n');
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('results.case.deltaTitle'),
      message: `${this.i18n.t('results.case.deltaMessage')}\n\n${lines}`,
      confirmLabel: this.i18n.t('results.case.acknowledgeVerify'),
      cancelLabel: this.i18n.t('results.cancel'),
    });
    if (!confirmed) {
      return;
    }

    const outcome = await this.facade.acknowledgeAndVerify.execute({ deltaIds: deltas.map((row) => row.id), verifyIds, comment: null });
    this.report(outcome, 'verified', this.facade.acknowledgeAndVerify.problem());
  }

  protected async undo(unit: ReportUnitView): Promise<void> {
    const ids = this.facade.undoable(unit);
    if (ids.length === 0) {
      this.toast.warn(this.i18n.t('results.case.nothingToUndo'));
      return;
    }

    const reason = await this.confirm.promptReason({
      title: this.i18n.t('results.case.undoTitle'),
      message: this.countText('results.case.undoMessage', ids.length),
      danger: true,
      confirmLabel: this.i18n.t('results.case.undo'),
    });
    if (reason === null || reason.length === 0) {
      return;
    }

    this.report(await this.facade.undo.execute({ ids, reason }), 'undone', this.facade.undo.problem());
  }

  protected async preview(unit: ReportUnitView): Promise<void> {
    const doc = await this.facade.preview.execute(unit.key);
    if (doc === null) {
      this.toast.error(problemMessage(this.i18n, 'results', this.facade.preview.problem()));
      return;
    }

    const [images, texts] = await Promise.all([this.templateImages.load(doc.template), this.printer.reportTexts(doc)]);
    this.previewTexts.set(texts);
    this.previewImages()?.release();
    this.previewImages.set(images);
    this.previewDoc.set(doc);
    this.previewOpen.set(true);
  }

  /** T8: the server marks the unit printed, then the official copy is rendered and sent to the printer. */
  protected async print(unit: ReportUnitView): Promise<void> {
    this.previewOpen.set(false);
    const doc = await this.facade.print.execute(unit.key);
    if (doc === null) {
      this.toast.error(problemMessage(this.i18n, 'results', this.facade.print.problem()));
      return;
    }

    this.toast.success(this.i18n.tf('results.case.printed', { accession: doc.visit.accession }));
    await this.printer.report(doc);
  }

  // ---- row actions -------------------------------------------------------------------------------------------------

  protected onDraft(change: DraftChange): void {
    this.facade.setDraft(change.row, change.value);
  }

  protected openHistory(row: ResultRowView): void {
    this.facade.openHistory(row, 'log');
  }

  protected closeHistory(): void {
    this.facade.closeHistory();
  }

  protected openComment(row: ResultRowView): void {
    this.commentRow.set(row);
    this.facade.commentSearch.set({ search: '', groupId: this.unit()?.clinicalGroupId ?? null });
    this.commentOpen.set(true);
  }

  protected searchComments(search: string): void {
    this.facade.commentSearch.set({ search, groupId: this.unit()?.clinicalGroupId ?? null });
  }

  protected async submitComment(input: { readonly row: ResultRowView; readonly text: string | null }): Promise<void> {
    await this.facade.comment.execute({ id: input.row.id, text: input.text });
    this.afterRowAction(this.facade.comment.problem(), 'results.case.commentSaved', () => this.commentOpen.set(false));
  }

  protected openNote(row: ResultRowView): void {
    this.noteRow.set(row);
    this.noteOpen.set(true);
  }

  protected async submitNote(text: string | null): Promise<void> {
    const row = this.noteRow();
    if (!row) {
      return;
    }

    await this.facade.note.execute({ id: row.id, text });
    this.afterRowAction(this.facade.note.problem(), 'results.case.noteSaved', () => this.noteOpen.set(false));
  }

  protected openAcknowledge(row: ResultRowView): void {
    this.deltaRow.set(row);
    this.deltaOpen.set(true);
  }

  protected async submitAcknowledge(comment: string | null): Promise<void> {
    const row = this.deltaRow();
    if (!row) {
      return;
    }

    await this.facade.acknowledge.execute({ id: row.id, comment });
    this.afterRowAction(this.facade.acknowledge.problem(), 'results.case.deltaAcknowledged', () => this.deltaOpen.set(false));
  }

  protected openAmend(row: ResultRowView): void {
    this.amendRow.set(row);
    this.amendOpen.set(true);
  }

  protected async submitAmend(request: AmendRequest): Promise<void> {
    await this.facade.amend.execute({ id: request.row.id, value: request.value, reason: request.reason });
    this.afterRowAction(this.facade.amend.problem(), 'results.case.amended', () => this.amendOpen.set(false));
  }

  protected openCulture(row: ResultRowView): void {
    this.facade.microbiologyWanted.set(true);
    this.cultureRow.set(row);
    this.cultureOpen.set(true);
  }

  protected async submitCulture(request: CultureRequest): Promise<void> {
    const outcome = await this.facade.report.execute({ row: request.row, paragraphs: null, culture: request.culture, reason: request.reason });
    this.afterReport(outcome, this.facade.report.problem(), () => this.cultureOpen.set(false));
  }

  protected openText(row: ResultRowView): void {
    this.textRow.set(row);
    this.textOpen.set(true);
  }

  protected async submitText(request: TextReportRequest): Promise<void> {
    const outcome = await this.facade.report.execute({ row: request.row, paragraphs: request.paragraphs, culture: null, reason: request.reason });
    this.afterReport(outcome, this.facade.report.problem(), () => this.textOpen.set(false));
  }

  // ---- labels ------------------------------------------------------------------------------------------------------

  /** The patient of the open case; a VIP file masked by the server (no See-VIP right, F-06) reads "VIP patient". */
  protected patientName(view: ResultCaseView): string {
    return view.patient.isMasked === true ? this.i18n.t('results.vipMasked') : pickName(view.patient.fullName, view.patient.nameAr, this.i18n.lang());
  }

  /** Printing a VIP patient's report needs the See-VIP right (RES-056): the button stays visible, disabled, with the reason. */
  protected printBlocked(view: ResultCaseView | null): boolean {
    return view?.patient.isMasked === true;
  }

  protected patientAge(view: ResultCaseView): string {
    const patient = view.patient;
    return ageText(patient.ageYears, patient.ageMonths, patient.ageDays, { y: this.i18n.t('results.age.y'), m: this.i18n.t('results.age.m'), d: this.i18n.t('results.age.d') }, (value) => this.num(value));
  }

  /** Counts, ages and percentages in the page language's digits (validation v0.5.0 F-09). */
  protected num(value: number): string {
    return screenNumber(value, this.i18n.lang());
  }

  protected gender(view: ResultCaseView): string {
    return view.patient.gender ? this.i18n.t(`results.gender.${view.patient.gender}`) : '—';
  }

  /** The patient of a matching visit; a VIP row masked by the server (rule A-03) reads the dictionary's "VIP patient". */
  protected visitLabel(item: ResultVisitListItem): string {
    return item.patientMasked === true ? this.i18n.t('results.vipMasked') : pickName(item.patientName, item.patientNameAr, this.i18n.lang());
  }

  protected unitName(unit: ReportUnitView): string {
    const lang = this.i18n.lang();
    if (unit.printAloneServiceId !== null) {
      return pickName(unit.serviceNameEn, unit.serviceNameAr, lang);
    }

    return pickName(unit.groupNameEn, unit.groupNameAr, lang);
  }

  protected testName(row: ResultRowView): string {
    return pickName(row.reportName ?? row.testNameEn, row.testNameAr, this.i18n.lang());
  }

  protected name(en: string | null, ar: string | null): string {
    return pickName(en, ar, this.i18n.lang());
  }

  protected at(value: string | null | undefined): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected statusLabel(status: ResultStatus | string): string {
    return this.i18n.t(`results.status.${status}`);
  }

  protected sampleStatusLabel(status: SampleStatusName | string): string {
    return this.i18n.t(`results.sampleStatus.${status}`);
  }

  // ---- outcome reporting -------------------------------------------------------------------------------------------

  private report(outcome: BulkResultOutcome | null, key: 'saved' | 'reviewed' | 'verified' | 'undone', problem: { code: string } | null): void {
    if (outcome === null) {
      this.toast.error(problemMessage(this.i18n, 'results', problem));
      return;
    }

    if (outcome.changed.length > 0) {
      this.toast.success(this.countText(`results.case.${key}`, outcome.changed.length));
    }

    if (outcome.refused.length > 0) {
      this.toast.warn(this.countText('results.case.refusedCount', outcome.refused.length));
    }
  }

  /**
   * A counted sentence in its grammatical form (`I18nService.tc`: one, two, 3–10, 11+; validation v0.5.0 F-11) with the
   * number in the page language's digits.
   */
  protected countText(key: string, count: number): string {
    return this.i18n.tc(key, count, { count: this.num(count) });
  }

  /**
   * The list heading: the count of the last query in the language's digits (round 3 R3-02); no count before a query has
   * run, so a visit opened from a link does not sit under "Matching visits (0)" (validation 2026-10-11).
   */
  protected matchingTitle(): string {
    return this.facade.query() === null
      ? this.i18n.t('results.case.matchingTitle')
      : this.i18n.tf('results.case.matching', { count: this.num(this.visitsTotal()) });
  }

  /** The matching-list line "1 report unit · 2 results": each count takes its own singular (round 2 R2-07). */
  protected unitsLine(item: Pick<ResultVisitListItem, 'units' | 'results'>): string {
    return `${this.countText('results.case.unitsPart', item.units)} · ${this.countText('results.case.resultsPart', item.results)}`;
  }

  private afterRowAction(problem: { code: string } | null, key: string, close: () => void): void {
    if (problem !== null) {
      this.toast.error(problemMessage(this.i18n, 'results', problem));
      return;
    }

    close();
    this.toast.success(this.i18n.t(key));
  }

  private afterReport(outcome: BulkResultOutcome | null | undefined, problem: { code: string } | null, close: () => void): void {
    if (problem !== null) {
      this.toast.error(problemMessage(this.i18n, 'results', problem));
      return;
    }

    if (outcome && outcome.refused.length > 0) {
      this.toast.error(problemMessage(this.i18n, 'results', { code: outcome.refused[0]?.code ?? 'INTERNAL-500' }));
      return;
    }

    close();
    this.toast.success(this.countText('results.case.saved', 1));
  }
}

import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, output } from '@angular/core';

import type { ReportUnitView, ResultRowView, ServiceBannerView } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { flagClass, flagSymbol, flagTone, initialDraft, isSigned, screenNumber } from './result-format';

/** One line of the grid: a profile banner or a result row. */
type GridLine = { readonly kind: 'banner'; readonly banner: ServiceBannerView } | { readonly kind: 'row'; readonly row: ResultRowView };

/** A value typed in a cell. */
export interface DraftChange {
  readonly row: ResultRowView;
  readonly value: string;
}

/**
 * The keyboard-first results grid of one report unit (CP-19; LDM §5.3): test, value by type (typed number or text, list
 * picker, read-only formula, culture and text-report buttons), unit, reference range, flag, delta badge with
 * acknowledgement, sample and test status, printed marker and the row actions (history, comment, note, amend). Enter moves to the
 * next value cell, Shift+Enter to the previous one. Rows of a profile sit under its banner (BR-04 banner status). Dumb:
 * values go out as `draftChange`, actions as outputs; the page saves.
 */
@Component({
  selector: 'app-result-grid',
  imports: [BadgeComponent, IconComponent],
  template: `
    <div class="table-wrap">
      <table class="grid">
        <caption class="visually-hidden">{{ i18n.t('results.grid.caption') }}</caption>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('results.grid.test') }}</th>
            <th scope="col" class="value-col">{{ i18n.t('results.grid.result') }}</th>
            <th scope="col">{{ i18n.t('results.grid.unit') }}</th>
            <th scope="col">{{ i18n.t('results.grid.range') }}</th>
            <th scope="col">{{ i18n.t('results.grid.flag') }}</th>
            <th scope="col">{{ i18n.t('results.grid.delta') }}</th>
            <th scope="col">{{ i18n.t('results.grid.sampleStatus') }}</th>
            <th scope="col">{{ i18n.t('results.grid.status') }}</th>
            <th scope="col">{{ i18n.t('results.grid.printed') }}</th>
            <th scope="col" class="actions-col"><span class="visually-hidden">{{ i18n.t('results.grid.actions') }}</span></th>
          </tr>
        </thead>
        <tbody>
          @for (line of lines(); track $index) {
            @if (line.kind === 'banner') {
              <tr class="banner">
                <th colspan="10" scope="colgroup">
                  {{ bannerName(line.banner) }}
                  <app-badge [status]="line.banner.status">{{ statusLabel(line.banner.status) }}</app-badge>
                </th>
              </tr>
            } @else {
              @let row = line.row;
              <tr [class]="rowClass(row)" [attr.data-result]="row.id">
                <th scope="row" class="test">
                  <span class="name">{{ testName(row) }}</span>
                  @if (row.isHiddenHelper) {
                    <span class="tag">{{ i18n.t('results.grid.helper') }}</span>
                  }
                  @if (row.source === 'Analyser') {
                    <span class="tag" [title]="row.analyserFlags ?? ''">{{ row.analyserCode }}</span>
                  }
                  @if (refusals()[row.id]; as code) {
                    <span class="refusal" role="alert">{{ refusalText(code) }}</span>
                  }
                </th>
                <td class="value-col">
                  @switch (row.testType) {
                    @case ('Formula') {
                      <span class="formula" [title]="i18n.t('results.grid.formulaHint')">ƒ {{ row.textValue ?? '—' }}</span>
                    }
                    @case ('Culture') {
                      <button type="button" class="btn sm soft" (click)="culture.emit(row)" [disabled]="!row.culture && locked(row)">
                        <app-icon name="bug" [size]="13" />{{ row.culture ? i18n.t('results.grid.editCulture') : i18n.t('results.grid.cultureEntry') }}
                      </button>
                      @if (row.textValue) {
                        <div class="small summary">{{ row.textValue }}</div>
                      }
                    }
                    @case ('FormattedText') {
                      <button type="button" class="btn sm soft" (click)="textReport.emit(row)" [disabled]="!row.paragraphs && locked(row)">
                        <app-icon name="clipboard" [size]="13" />{{ row.paragraphs ? i18n.t('results.grid.editText') : i18n.t('results.grid.textEntry') }}
                      </button>
                      @if (row.paragraphs?.[0]; as first) {
                        <div class="small summary">{{ first.title }}: {{ first.text }}</div>
                      }
                    }
                    @case ('RestrictedList') {
                      <select class="inp sm value-input" [value]="draft(row)" [disabled]="locked(row)" [attr.aria-label]="testName(row)" (change)="changed(row, $event)" (keydown)="move($event)">
                        <option value="">—</option>
                        @for (option of usable(row); track option.value) {
                          <option [value]="option.value">{{ option.value }}</option>
                        }
                      </select>
                    }
                    @case ('OptionalList') {
                      <input class="inp sm value-input" type="text" [attr.list]="'list-' + row.id" [value]="draft(row)" [disabled]="locked(row)" [attr.aria-label]="testName(row)" (input)="changed(row, $event)" (keydown)="move($event)" maxlength="500" autocomplete="off" />
                      <datalist [id]="'list-' + row.id">
                        @for (option of usable(row); track option.value) {
                          <option [value]="option.value"></option>
                        }
                      </datalist>
                    }
                    @default {
                      <input class="inp sm value-input" type="text" [attr.inputmode]="row.testType === 'Numeric' ? 'decimal' : null" [value]="draft(row)" [disabled]="locked(row)" [attr.aria-label]="testName(row)" (input)="changed(row, $event)" (keydown)="move($event)" maxlength="500" autocomplete="off" />
                    }
                  }
                  @if (row.isPanic) {
                    <span class="panic" role="status">{{ i18n.t('results.grid.panic') }}</span>
                  }
                </td>
                <td class="small">{{ row.unit ?? '' }}</td>
                <td class="small range">{{ row.range?.display ?? '' }}</td>
                <td>
                  @if (symbol(row); as flag) {
                    <app-badge [tone]="tone(row)">{{ flag }}</app-badge>
                  }
                </td>
                <td>
                  @if (row.delta; as delta) {
                    @if (delta.flag) {
                      <app-badge tone="warn" [attr.title]="deltaTitle(row)">Δ {{ percent(delta.percent) }}</app-badge>
                      @if (!delta.acknowledged && canAcknowledge()) {
                        <button type="button" class="btn sm ghost" (click)="acknowledge.emit(row)">{{ i18n.t('results.grid.acknowledge') }}</button>
                      } @else if (delta.acknowledged) {
                        <app-icon name="check" [size]="13" />
                      }
                    }
                  }
                </td>
                <td>
                  @if (row.sampleStatus) {
                    <app-badge [status]="row.sampleStatus">{{ sampleStatusLabel(row.sampleStatus) }}</app-badge>
                  } @else {
                    —
                  }
                </td>
                <td><app-badge [status]="row.status">{{ statusLabel(row.status) }}</app-badge></td>
                <td>
                  @if (row.printCount > 0) {
                    <app-icon name="printer" [size]="14" />
                  }
                </td>
                <td class="actions">
                  <button type="button" class="icon-btn" [title]="i18n.t('results.grid.history')" [attr.aria-label]="i18n.t('results.grid.history')" (click)="history.emit(row)"><app-icon name="clipboard" [size]="15" /></button>
                  @if (canEdit()) {
                    <button type="button" class="icon-btn" [class.flagged]="!!row.reportComment" [title]="i18n.t('results.grid.comment')" [attr.aria-label]="i18n.t('results.grid.comment')" (click)="comment.emit(row)"><app-icon name="edit" [size]="15" /></button>
                  }
                  @if (canAnnotate()) {
                    <button type="button" class="icon-btn" [class.flagged]="!!row.internalNote" [title]="i18n.t('results.grid.note')" [attr.aria-label]="i18n.t('results.grid.note')" (click)="note.emit(row)"><app-icon name="inbox" [size]="15" /></button>
                  }
                  @if (canAmend() && isSigned(row.status) && row.testType !== 'Formula') {
                    <button type="button" class="btn sm" (click)="amend.emit(row)">{{ i18n.t('results.grid.amend') }}</button>
                  }
                </td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    .table-wrap { overflow-x: auto; }
    .grid { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); }
    .grid th, .grid td { padding: 6px 8px; border-block-end: 1px solid var(--line-soft); text-align: start; vertical-align: middle; }
    .grid thead th { font-weight: 600; color: var(--muted); white-space: nowrap; }
    .grid .banner th { background: var(--surface-2); font-weight: 600; }
    .grid .test { font-weight: 500; }
    .tag { display: inline-block; margin-inline-start: 6px; padding: 0 6px; border-radius: var(--r-pill); background: var(--surface-2); color: var(--muted); font-size: var(--fs-xs); font-weight: 500; }
    .value-col { min-width: 170px; }
    .value-input { width: 100%; max-width: 220px; }
    .formula { color: var(--violet); font-weight: 600; }
    .summary { color: var(--muted); max-width: 260px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .panic { display: inline-block; margin-inline-start: 6px; color: var(--panic); font-weight: 700; }
    .refusal { display: block; color: var(--bad); font-size: var(--fs-xs); font-weight: 500; }
    .range { white-space: nowrap; }
    .actions { white-space: nowrap; text-align: end; }
    .icon-btn.flagged { color: var(--violet); }
    tr.flag-high td.value-col, tr.flag-high .value-input { background: var(--bad-soft); }
    tr.flag-low td.value-col, tr.flag-low .value-input { background: var(--warn-soft); }
    tr.flag-panic td.value-col, tr.flag-panic .value-input { background: var(--panic-soft); }
    tr.flag-abnormal td.value-col { background: var(--warn-soft); }
    tr.dirty .value-input { outline: 2px solid var(--blue); outline-offset: -2px; }
    tr.hidden-helper .name { color: var(--muted); font-style: italic; }
    :host(.compact) .grid th, :host(.compact) .grid td { padding: 1px 8px; }
    :host(.compact) .value-input { min-height: 26px; padding-block: 1px; }
    :host(.compact) .icon-btn { padding: 2px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.compact]': "density() === 'compact'" },
})
export class ResultGridComponent {
  protected readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly unit = input.required<ReportUnitView>();
  /** The row density (results page, 2026-10-14): `compact` fits a long profile on one screen; a preference of the user. */
  readonly density = input<'comfortable' | 'compact'>('comfortable');
  readonly drafts = input<Readonly<Record<string, string>>>({});
  readonly refusals = input<Readonly<Record<string, string>>>({});
  /** The user may enter values (results.create and the unit's edit right). */
  readonly canEdit = input(false);
  /**
   * The user may write internal notes (results.create; the server also needs the "see" right, which showing the unit
   * implies). The report comment follows `canEdit` (results.create and the unit's edit right), as the server requires
   * (UAT A-02: a view-only user was offered both dialogs and the save was refused).
   */
  readonly canAnnotate = input(false);
  /** The user may amend signed results (results.edit and the special right). */
  readonly canAmend = input(false);
  readonly canAcknowledge = input(false);

  readonly draftChange = output<DraftChange>();
  readonly culture = output<ResultRowView>();
  readonly textReport = output<ResultRowView>();
  readonly history = output<ResultRowView>();
  readonly comment = output<ResultRowView>();
  readonly note = output<ResultRowView>();
  readonly acknowledge = output<ResultRowView>();
  readonly amend = output<ResultRowView>();

  protected readonly isSigned = isSigned;

  protected readonly lines = computed<readonly GridLine[]>(() => {
    const unit = this.unit();
    const showBanners = unit.services.length > 1 || unit.services.some((service) => service.resultCount > 1);
    const lines: GridLine[] = [];
    for (const banner of unit.services) {
      const rows = unit.results.filter((row) => row.serviceId === banner.serviceId);
      if (showBanners && rows.length > 1) {
        lines.push({ kind: 'banner', banner });
      }

      lines.push(...rows.map((row): GridLine => ({ kind: 'row', row })));
    }

    const listed = new Set(lines.flatMap((line) => (line.kind === 'row' ? [line.row.id] : [])));
    lines.push(...unit.results.filter((row) => !listed.has(row.id)).map((row): GridLine => ({ kind: 'row', row })));
    return lines;
  });

  protected draft(row: ResultRowView): string {
    return this.drafts()[row.id] ?? initialDraft(row);
  }

  protected locked(row: ResultRowView): boolean {
    return !this.canEdit() || isSigned(row.status) || row.status === 'Cancelled' || !row.sampleReceived;
  }

  protected usable(row: ResultRowView): ResultRowView['listOptions'] {
    return row.listOptions.filter((option) => !option.isPlaceholder);
  }

  protected changed(row: ResultRowView, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLSelectElement;
    this.draftChange.emit({ row, value: target.value });
  }

  /** Enter → next value cell, Shift+Enter → previous (LDM keyboard entry). */
  protected move(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }

    const cells = Array.from(this.host.querySelectorAll<HTMLInputElement | HTMLSelectElement>('.value-input:not([disabled])'));
    const index = cells.indexOf(event.target as HTMLInputElement);
    const next = cells[index + (event.shiftKey ? -1 : 1)];
    if (next) {
      event.preventDefault();
      next.focus();
      if (next instanceof HTMLInputElement) {
        next.select();
      }
    }
  }

  protected rowClass(row: ResultRowView): string {
    const classes = [flagClass(row.flag)];
    if (this.drafts()[row.id] !== undefined) {
      classes.push('dirty');
    }

    if (row.isHiddenHelper) {
      classes.push('hidden-helper');
    }

    return classes.filter((value) => value.length > 0).join(' ');
  }

  protected testName(row: ResultRowView): string {
    const ar = this.i18n.lang() === 'ar';
    return (ar ? row.testNameAr : row.reportName ?? row.testNameEn) ?? row.testCode ?? row.testId;
  }

  protected bannerName(banner: ServiceBannerView): string {
    return (this.i18n.lang() === 'ar' ? banner.nameAr : banner.reportName ?? banner.nameEn) ?? banner.code ?? '';
  }

  protected symbol(row: ResultRowView): string {
    return flagSymbol(row.flag);
  }

  protected tone(row: ResultRowView): ReturnType<typeof flagTone> {
    return flagTone(row.flag);
  }

  protected deltaTitle(row: ResultRowView): string {
    const percent = row.delta?.percent;
    return this.i18n.tf('results.grid.deltaTitle', { previous: row.delta?.previousValue ?? '—', percent: percent === null || percent === undefined ? '—' : screenNumber(percent, this.i18n.lang()) });
  }

  /** "Δ 25%" / "Δ ٢٥٪" in the page language's digits (validation v0.5.0 F-09). */
  protected percent(value: number | null): string {
    return value === null ? '' : this.i18n.tf('results.worklist.percent', { value: screenNumber(value, this.i18n.lang()) });
  }

  protected statusLabel(status: string): string {
    return this.i18n.t(`results.status.${status}`);
  }

  protected sampleStatusLabel(status: string): string {
    return this.i18n.t(`results.sampleStatus.${status}`);
  }

  protected refusalText(code: string): string {
    const key = `results.error.${code}`;
    return this.i18n.has(key) ? this.i18n.t(key) : code;
  }
}

import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { type LabTrackerDelayState, type LabTrackerServiceItem, TRACKER_LINKS } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { dateCell } from '../../../shared/export/table-export';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn, TableSort } from '../../../shared/ui/data-table/data-table.types';
import { TrackerRowActionsComponent } from './row-actions.component';
import { ageText, dateTime, duration, num, patientName, pickName } from './tracker-format';

/**
 * The Lab Tracker's *Services* rows read like the worklist (CP-18; change request of 2026-10-26): the same columns in the same
 * order (patient no., accession, sample, patient, age / sex, service with its clinical group, test status, sample status,
 * collected, expected result, delay, consumed share), the same row colours and legend by the tenant's delay thresholds, plus
 * the tracker's row actions, column chooser (saved per user under its own list key) and *Print* / *Excel*.
 */
@Component({
  selector: 'app-tracker-service-table',
  imports: [RouterLink, DataTableComponent, CellTemplateDirective, RowActionsTemplateDirective, BadgeComponent, TrackerRowActionsComponent],
  template: `
    <p class="legend" aria-hidden="true">
      <span><i class="sw warn"></i>{{ i18n.tf('tracker.service.legendWarn', { percent: count(warnPercent()) }) }}</span>
      <span><i class="sw late"></i>{{ i18n.tf('tracker.service.legendLate', { percent: count(latePercent()) }) }}</span>
      <span><i class="sw done"></i>{{ i18n.t('tracker.service.legendDone') }}</span>
    </p>
    <app-data-table
      [columns]="columns()"
      [rows]="rows()"
      [rowId]="rowId"
      [rowClass]="rowClass"
      [loading]="loading()"
      [interactive]="false"
      [emptyTitle]="i18n.t('tracker.empty')"
      [emptyMessage]="i18n.t('tracker.emptyHint')"
      [caption]="i18n.t('tracker.title')"
      [compact]="true"
      [sort]="sort()"
      [serverSort]="true"
      (sortChange)="sortChange.emit($event)"
      listKey="tracker-services"
      selection="multi"
      [exportable]="true"
      [printSubtitle]="printSubtitle()"
    >
      <ng-template appCell="accession" [appCellOf]="rows()" let-row>
        <a class="link mono" [routerLink]="accessionLink" [queryParams]="{ visitId: row.visitId, returnTo: returnTo() }">{{ row.accession }}</a>
        @if (row.urgent) {
          <app-badge tone="bad">{{ i18n.t('tracker.urgent') }}</app-badge>
        }
      </ng-template>
      <ng-template appCell="service" [appCellOf]="rows()" let-row>
        <span>{{ service(row) }}</span>
        @if (group(row); as group) {
          <span class="sub">{{ group }}</span>
        }
      </ng-template>
      <ng-template appCell="status" [appCellOf]="rows()" let-row>
        @if (row.resultStatus) {
          <app-badge [status]="row.resultStatus">{{ i18n.t('tracker.service.status.' + row.resultStatus) }}</app-badge>
        } @else {
          —
        }
      </ng-template>
      <ng-template appCell="sampleStatus" [appCellOf]="rows()" let-row>
        @if (row.sampleStatus) {
          <app-badge [status]="row.sampleStatus">{{ i18n.t('tracker.service.sampleStatus.' + row.sampleStatus) }}</app-badge>
        } @else {
          —
        }
      </ng-template>
      <ng-template appCell="delay" [appCellOf]="rows()" let-row>
        @if (delayText(row); as text) {
          <app-badge [tone]="delayTone(row.turnaroundState)">{{ text }}</app-badge>
        } @else {
          —
        }
      </ng-template>
      <ng-template appCell="consumed" [appCellOf]="rows()" let-row>
        @if (row.consumedPercent !== null && row.consumedPercent !== undefined) {
          <span class="consumed" [class]="'consumed ' + (rowClass(row) ?? '')">
            <span class="bar"><span class="fill" [style.width.%]="row.consumedPercent > 100 ? 100 : row.consumedPercent"></span></span>
            <span class="sub">{{ percent(row.consumedPercent) }}</span>
          </span>
        } @else {
          —
        }
      </ng-template>
      <ng-template appRowActions [appRowActionsOf]="rows()" let-row>
        <app-tracker-row-actions [item]="row" [returnTo]="returnTo()" />
      </ng-template>
    </app-data-table>
  `,
  styles: `
    .legend { display: flex; flex-wrap: wrap; gap: var(--sp-3); margin: 0; padding: var(--sp-2) var(--sp-4); font-size: var(--fs-xs); color: var(--muted); }
    .legend > span { display: inline-flex; align-items: center; gap: 4px; }
    .sw { display: inline-block; width: 12px; height: 12px; border-radius: 3px; border: 1px solid var(--line); }
    .sw.warn, :host ::ng-deep tr.delay-warn td { background: var(--warn-soft); }
    .sw.late, :host ::ng-deep tr.delay-late td { background: var(--bad-soft); }
    .sw.done, :host ::ng-deep tr.delay-done td { background: var(--ok-soft); }
    .link { font-weight: 600; color: var(--blue); text-decoration: none; }
    .link:hover { text-decoration: underline; }
    .sub { display: block; font-size: var(--fs-xs); color: var(--muted); }
    .consumed { display: inline-flex; align-items: center; gap: 6px; min-width: 90px; }
    .consumed .sub { display: inline; }
    .bar { flex: 1 1 auto; height: 6px; border-radius: var(--r-pill); background: var(--line-soft); overflow: hidden; }
    .fill { display: block; height: 100%; background: var(--blue); }
    .consumed.delay-warn .fill { background: var(--warn); }
    .consumed.delay-late .fill { background: var(--bad); }
    .consumed.delay-done .fill { background: var(--ok); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrackerServiceTableComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly accessionLink = TRACKER_LINKS.accession;

  readonly rows = input.required<readonly LabTrackerServiceItem[]>();
  readonly loading = input(false);
  readonly sort = input<TableSort | null>(null);
  readonly warnPercent = input(80);
  readonly latePercent = input(100);
  readonly returnTo = input<string | null>(null);
  readonly printSubtitle = input<string | null>(null);
  readonly sortChange = output<TableSort | null>();

  /** The worklist's columns, in its order; the secondary ones are optional for the column chooser. */
  protected readonly columns = computed<readonly TableColumn<LabTrackerServiceItem>[]>(() => {
    const lang = this.i18n.lang();
    return [
      { key: 'patientNumber', header: 'tracker.service.column.patientNumber', mono: true, width: '140px', secondary: true, optional: true, value: (row) => row.patientNumber },
      { key: 'accession', header: 'tracker.service.column.accession', mono: true, width: '140px', sortable: true, exportValue: (row) => (row.urgent ? `${row.accession} · ${this.i18n.t('tracker.urgent')}` : row.accession) },
      { key: 'sampleNumber', header: 'tracker.service.column.sample', mono: true, width: '140px', secondary: true, optional: true, value: (row) => this.sample(row) },
      { key: 'patient', header: 'tracker.service.column.patient', sortable: true, value: (row) => patientName(row, this.i18n) },
      { key: 'ageSex', header: 'tracker.service.column.ageSex', width: '90px', secondary: true, optional: true, value: (row) => this.ageSex(row) },
      { key: 'service', header: 'tracker.service.column.service', sortable: true, exportValue: (row) => [this.service(row), this.group(row)].filter((part) => !!part).join(' · ') },
      { key: 'status', header: 'tracker.service.column.status', width: '110px', sortable: true, exportValue: (row) => (row.resultStatus ? this.i18n.t('tracker.service.status.' + row.resultStatus) : '') },
      {
        key: 'sampleStatus',
        header: 'tracker.service.column.sampleStatus',
        width: '110px',
        secondary: true,
        optional: true,
        exportValue: (row) => (row.sampleStatus ? this.i18n.t('tracker.service.sampleStatus.' + row.sampleStatus) : ''),
      },
      {
        key: 'collected',
        header: 'tracker.service.column.collected',
        width: '140px',
        secondary: true,
        optional: true,
        value: (row) => (row.collectedAtUtc ? dateTime(row.collectedAtUtc, lang) : null),
        exportValue: (row) => (row.collectedAtUtc ? dateCell(row.collectedAtUtc, dateTime(row.collectedAtUtc, lang)) : null),
      },
      {
        key: 'expected',
        header: 'tracker.service.column.expected',
        width: '140px',
        sortable: true,
        value: (row) => (row.expectedAtUtc ? dateTime(row.expectedAtUtc, lang) : null),
        exportValue: (row) => (row.expectedAtUtc ? dateCell(row.expectedAtUtc, dateTime(row.expectedAtUtc, lang)) : null),
      },
      { key: 'delay', header: 'tracker.service.column.delay', width: '110px', sortable: true, exportValue: (row) => this.delayText(row) ?? '' },
      // not sortable, as on the worklist: a ratio of intervals the server does not order by
      { key: 'consumed', header: 'tracker.service.column.consumed', width: '120px', exportValue: (row) => (row.consumedPercent === null || row.consumedPercent === undefined ? '' : this.percent(row.consumedPercent)) },
    ];
  });

  protected readonly rowId = (row: LabTrackerServiceItem): string => row.lineId;

  /** The worklist's row colour: at risk, late, or done (verified / printed). */
  protected readonly rowClass = (row: LabTrackerServiceItem): string | null => {
    switch (row.turnaroundState) {
      case 'Warning':
        return 'delay-warn';
      case 'Late':
        return 'delay-late';
      case 'Done':
        return 'delay-done';
      default:
        return null;
    }
  };

  /** "GLU · Glucose" in the page language. */
  protected service(row: LabTrackerServiceItem): string {
    return `${row.serviceCode} · ${pickName(row.serviceName, row.serviceNameAr, this.i18n.lang()) ?? row.serviceName}`;
  }

  protected group(row: LabTrackerServiceItem): string | null {
    return pickName(row.groupNameEn ?? null, row.groupNameAr ?? null, this.i18n.lang());
  }

  /** The first sample of the line, "+2" when it has more. */
  protected sample(row: LabTrackerServiceItem): string | null {
    const more = (row.sampleCount ?? 0) - 1;
    return row.sampleNumber ? (more > 0 ? `${row.sampleNumber} ${this.i18n.tf('tracker.service.moreSamples', { count: this.count(more) })}` : row.sampleNumber) : null;
  }

  protected ageSex(row: LabTrackerServiceItem): string {
    const parts: string[] = [];
    if (row.ageText) {
      parts.push(ageText(row.ageText, this.i18n));
    }

    if (row.gender) {
      const key = `tracker.gender.${row.gender}`;
      parts.push(this.i18n.has(key) ? this.i18n.t(key) : row.gender);
    }

    return parts.join(' · ');
  }

  /** The time past the expected result, as the worklist shows it once a minute is passed and the line is not done. */
  protected delayText(row: LabTrackerServiceItem): string | null {
    const seconds = row.delaySeconds ?? 0;
    return seconds >= 60 && row.turnaroundState !== 'Done' ? duration(Math.floor(seconds / 60), this.i18n) : null;
  }

  protected delayTone(state: LabTrackerDelayState | null | undefined): BadgeTone {
    return state === 'Late' ? 'bad' : state === 'Warning' ? 'warn' : state === 'Done' ? 'ok' : 'info';
  }

  protected percent(value: number): string {
    return this.i18n.tf('tracker.service.percent', { value: this.count(Math.floor(value)) });
  }

  protected count(value: number): string {
    return num(value, this.i18n.lang());
  }
}

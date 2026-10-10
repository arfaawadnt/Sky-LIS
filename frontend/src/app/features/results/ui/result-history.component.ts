import { ChangeDetectionStrategy, Component, computed, inject, input, model } from '@angular/core';

import type { PreviousResultItem, ResultLogItem, ResultRowView } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { flagSymbol, formatDateTime, logValueText, pickName } from './result-format';

export type HistoryTab = 'log' | 'status' | 'previous';

/**
 * The history of one result (LDM §5.3 "RL" result log, "SL" status log, "Last Result"): value changes with source
 * and analyser, status changes with reasons, and the patient's previous verified results of the test (delta
 * context, BR-11). Dumb: lists come in, the tab is a model.
 */
@Component({
  selector: 'app-result-history',
  imports: [BadgeComponent, TabsComponent],
  template: `
    @if (row(); as current) {
      <p class="muted small">{{ testName(current) }} · {{ i18n.t('results.status.' + current.status) }}</p>
    }
    <app-tabs [tabs]="tabs()" [(active)]="tab" />
    @switch (tab()) {
      @case ('status') {
        <ul class="entries" role="tabpanel">
          @for (entry of statusEntries(); track entry.id) {
            <li>
              <div class="when">{{ at(entry.atUtc) }} · {{ entry.byName ?? '—' }}</div>
              <div>
                @if (entry.fromStatus) {
                  <app-badge [status]="entry.fromStatus">{{ i18n.t('results.status.' + entry.fromStatus) }}</app-badge>
                  <span class="arrow">→</span>
                }
                @if (entry.toStatus) {
                  <app-badge [status]="entry.toStatus">{{ i18n.t('results.status.' + entry.toStatus) }}</app-badge>
                }
              </div>
              @if (entry.detail) {
                <div class="small">{{ entry.detail }}</div>
              }
            </li>
          } @empty {
            <li class="muted small">{{ i18n.t('results.history.empty') }}</li>
          }
        </ul>
      }
      @case ('previous') {
        <table class="previous" role="tabpanel">
          <caption class="visually-hidden">{{ i18n.t('results.history.previous') }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('results.history.visit') }}</th>
              <th scope="col">{{ i18n.t('results.grid.result') }}</th>
              <th scope="col">{{ i18n.t('results.grid.flag') }}</th>
              <th scope="col">{{ i18n.t('results.history.verifiedAt') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (item of previous(); track item.resultId) {
              <tr>
                <td><span class="mono">{{ item.accession }}</span><div class="small muted">{{ at(item.registeredAtUtc) }}</div></td>
                <td class="value">{{ item.textValue ?? item.numericValue ?? '—' }} {{ item.unit ?? '' }}</td>
                <td>{{ symbol(item) }}</td>
                <td class="small">{{ at(item.verifiedAtUtc) }}</td>
              </tr>
            } @empty {
              <tr><td colspan="4" class="muted small">{{ i18n.t('results.history.noPrevious') }}</td></tr>
            }
          </tbody>
        </table>
      }
      @default {
        <ul class="entries" role="tabpanel">
          @for (entry of logEntries(); track entry.id) {
            <li>
              <div class="when">{{ at(entry.atUtc) }} · {{ entry.byName ?? entry.source ?? '—' }}</div>
              <div>
                <span class="kind">{{ i18n.t('results.logKind.' + entry.kind) }}</span>
                @if (entry.kind === 'Value') {
                  <span class="value">{{ valueText(entry) }}</span>
                  @if (entry.source) {
                    <span class="tag">{{ entry.source }}</span>
                  }
                }
              </div>
              @if (entry.detail) {
                <div class="small">{{ entry.detail }}</div>
              }
            </li>
          } @empty {
            <li class="muted small">{{ i18n.t('results.history.empty') }}</li>
          }
        </ul>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .entries { list-style: none; margin: var(--sp-3) 0 0; padding: 0; display: grid; gap: var(--sp-2); }
    .entries li { padding: var(--sp-2) 0; border-block-end: 1px solid var(--line-soft); }
    .when { font-size: var(--fs-xs); color: var(--muted); }
    .kind { font-weight: 600; margin-inline-end: 6px; }
    .arrow { margin: 0 6px; color: var(--muted); }
    .tag { display: inline-block; margin-inline-start: 6px; padding: 0 6px; border-radius: var(--r-pill); background: var(--surface-2); color: var(--muted); font-size: var(--fs-xs); }
    .previous { width: 100%; border-collapse: collapse; margin-top: var(--sp-3); font-size: var(--fs-sm); }
    .previous th, .previous td { padding: 6px 8px; border-block-end: 1px solid var(--line-soft); text-align: start; vertical-align: top; }
    .value { font-weight: 600; font-variant-numeric: tabular-nums; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResultHistoryComponent {
  protected readonly i18n = inject(I18nService);

  readonly row = input<ResultRowView | null>(null);
  readonly log = input<readonly ResultLogItem[]>([]);
  readonly previous = input<readonly PreviousResultItem[]>([]);
  readonly tab = model<HistoryTab>('log');

  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { id: 'log', label: this.i18n.t('results.history.log'), badge: this.logEntries().length },
    { id: 'status', label: this.i18n.t('results.history.status'), badge: this.statusEntries().length },
    { id: 'previous', label: this.i18n.t('results.history.previous'), badge: this.previous().length },
  ]);

  protected readonly statusEntries = computed(() => this.log().filter((entry) => entry.kind === 'Status' || entry.kind === 'Ordered'));

  protected readonly logEntries = computed(() => this.log().filter((entry) => entry.kind !== 'Status' && entry.kind !== 'Ordered'));

  protected valueText(entry: ResultLogItem): string {
    return logValueText(entry.valueBefore, entry.valueAfter);
  }

  protected symbol(item: PreviousResultItem): string {
    return flagSymbol(item.flag);
  }

  protected at(value: string | null | undefined): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected testName(row: ResultRowView): string {
    return pickName(row.reportName ?? row.testNameEn, row.testNameAr, this.i18n.lang());
  }
}

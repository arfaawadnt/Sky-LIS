import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { REGISTRATION_PERMISSIONS, type VisitLineItem, type VisitLineKind, type VisitTubeItem } from '../../../core/api/registration.contracts';
import type { ResultStatus } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { followsLine, formulaCodeOf } from '../data/line-kinds';
import { dateTime, money, pick } from '../data/registration-format';

/**
 * The *Ordered tests* card of Accession details (CP-10b): every line of the visit in order (package members indented
 * under their package), code, name, type, sample number and type, line status — cancelled rows highlighted and kept
 * with their reason —, prices, expected result, the result status the Results module reports, and *Cancel* on a live
 * top-level line (`registration.cancel`; the page opens the cancel dialog with the row preselected).
 */
@Component({
  selector: 'app-reg-accession-lines',
  imports: [BadgeComponent, CanDirective, IconComponent, SectionCardComponent],
  template: `
    <app-section-card [title]="i18n.t('registration.accession.lines.title')" [subtitle]="i18n.tf('registration.accession.lines.count', { count: lines().length })" [padded]="false">
      @if (lines().length === 0) {
        <p class="muted small empty">{{ i18n.t('registration.accession.lines.empty') }}</p>
      } @else {
        <div class="scroll">
          <table class="grid">
            <thead>
              <tr>
                <th>{{ i18n.t('registration.column.code') }}</th>
                <th>{{ i18n.t('registration.column.service') }}</th>
                <th>{{ i18n.t('registration.accession.column.type') }}</th>
                <th>{{ i18n.t('registration.column.sample') }}</th>
                <th>{{ i18n.t('registration.column.status') }}</th>
                <th class="num">{{ i18n.t('registration.column.listPrice') }}</th>
                <th class="num">{{ i18n.t('registration.column.patientShare') }}</th>
                <th class="num">{{ i18n.t('registration.column.payerShare') }}</th>
                <th>{{ i18n.t('registration.column.expected') }}</th>
                <th>{{ i18n.t('registration.accession.column.result') }}</th>
                <th><span class="visually-hidden">{{ i18n.t('registration.column.actions') }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (line of ordered(); track line.id) {
                <tr [class.cancelled]="line.status === 'Cancelled'" [class.member]="follows(line.kind)" [attr.data-line]="line.id">
                  <td class="mono">{{ line.code }}</td>
                  <td>
                    {{ name(line.nameEn, line.nameAr) }}
                    @if (formulaOf(line); as formula) {
                      <span class="small muted block" [attr.data-formula]="line.id">{{ i18n.tf('registration.forFormula', { code: formula }) }}@if (line.visibleOnReceipt === false) { · {{ i18n.t('registration.offReceipt') }}}</span>
                    }
                    @if (line.status === 'Cancelled') {
                      <span class="small muted block">{{ line.cancellationReason ?? '' }} · {{ line.cancelledByName ?? '' }} · {{ when(line.cancelledAtUtc) }}</span>
                    }
                  </td>
                  <td class="small">{{ line.serviceType }}</td>
                  <td class="small">
                    @if (line.sampleNumber) {
                      <span class="mono">{{ line.sampleNumber }}</span>
                      <span class="muted block">{{ tubeType(line.tubeId) }}</span>
                    } @else {
                      <span class="muted">—</span>
                    }
                  </td>
                  <td><app-badge [status]="line.status">{{ i18n.t('registration.lineStatus.' + line.status) }}</app-badge></td>
                  <td class="num">{{ amount(line.listPrice) }}</td>
                  <td class="num">{{ amount(line.patientShare) }}</td>
                  <td class="num">{{ amount(line.payerShare) }}</td>
                  <td class="small">{{ when(line.expectedResultAtUtc) }}</td>
                  <td>
                    @if (resultOf(line.id); as status) {
                      <app-badge [status]="status">{{ i18n.t('shared.status.' + status) }}</app-badge>
                    } @else {
                      <span class="muted">—</span>
                    }
                  </td>
                  <td class="actions">
                    @if (!follows(line.kind) && line.status !== 'Cancelled' && lockedLines().has(line.id)) {
                      <span class="small muted" [attr.data-locked]="line.id">{{ i18n.t('registration.accession.lines.locked') }}</span>
                    } @else if (!follows(line.kind) && line.status !== 'Cancelled') {
                      <button type="button" class="btn sm danger" *appCan="permissions.cancel" (click)="cancelLine.emit(line)" [attr.aria-label]="i18n.t('registration.cancel.action') + ' ' + line.code"><app-icon name="x" [size]="14" />{{ i18n.t('registration.cancel.action') }}</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </app-section-card>
  `,
  styles: `
    .empty { padding: var(--sp-3) var(--sp-4); }
    .scroll { overflow-x: auto; }
    .grid { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); }
    .grid th, .grid td { padding: var(--sp-2) var(--sp-3); border-block-end: 1px solid var(--line-soft); text-align: start; vertical-align: top; }
    .grid th { color: var(--ink-2); font-weight: 600; white-space: nowrap; }
    .num { text-align: end !important; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .block { display: block; }
    .member td:first-child { padding-inline-start: var(--sp-5); }
    .member td:first-child::before { content: '└ '; color: var(--muted); }
    .cancelled td { background: var(--surface-2); color: var(--muted); }
    .cancelled td.mono, .cancelled td:nth-child(2) { text-decoration: line-through; }
    .cancelled td:nth-child(2) .block { text-decoration: none; }
    .actions { white-space: nowrap; text-align: end; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessionLinesComponent {
  protected readonly i18n = inject(I18nService);

  readonly lines = input<readonly VisitLineItem[]>([]);
  readonly tubes = input<readonly VisitTubeItem[]>([]);
  readonly resultStatus = input<ReadonlyMap<string, ResultStatus>>(new Map());
  /** Lines with a verified or printed result: no *Cancel* (REG-106), a note instead. */
  readonly lockedLines = input<ReadonlySet<string>>(new Set());
  readonly cancelLine = output<VisitLineItem>();

  protected readonly permissions = REGISTRATION_PERMISSIONS;

  /** Top-level lines in order, each package followed by its members, each line by the formula operand lines ordered for it (LIS-CR-015). */
  protected readonly ordered = computed<readonly VisitLineItem[]>(() => {
    const all = this.lines();
    const under = (id: string, kind: VisitLineKind): VisitLineItem[] => all.filter((child) => child.kind === kind && child.parentLineId === id);
    const out: VisitLineItem[] = [];
    for (const line of all) {
      if (!followsLine(line.kind)) {
        out.push(line, ...under(line.id, 'FormulaOperand'));
        for (const member of under(line.id, 'PackageMember')) {
          out.push(member, ...under(member.id, 'FormulaOperand'));
        }
      }
    }

    // members whose package is not in the list (never expected) are still shown rather than lost
    return out.length === all.length ? out : [...out, ...all.filter((line) => !out.includes(line))];
  });

  protected follows(kind: VisitLineKind): boolean {
    return followsLine(kind);
  }

  protected formulaOf(line: VisitLineItem): string | null {
    return formulaCodeOf(line, this.lines());
  }

  private readonly tubeTypes = computed(() => new Map(this.tubes().map((tube) => [tube.id, pick(tube.sampleTypeNameEn, tube.sampleTypeNameAr, this.i18n.lang())])));

  protected tubeType(tubeId: string | null): string {
    return tubeId === null ? '' : (this.tubeTypes().get(tubeId) ?? '');
  }

  protected resultOf(lineId: string): ResultStatus | null {
    return this.resultStatus().get(lineId) ?? null;
  }

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected amount(value: number): string {
    return money(value, this.i18n.lang());
  }

  protected when(utc: string | null): string {
    return dateTime(utc, this.i18n.lang());
  }
}

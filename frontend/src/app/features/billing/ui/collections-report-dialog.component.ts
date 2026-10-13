import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { map } from 'rxjs';

import {
  COLLECTIONS_REPORT_GROUPINGS,
  type CollectionsReportDocument,
  type CollectionsReportGrouping,
  type CollectionsReportPrivileges,
  type CollectionsReportRequest,
  type CollectionsReportRow,
  type CollectionsReportScope,
} from '../../../core/api/billing.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { formatCount, formatMoney } from './billing-format';
import { collectionsRowLabel, dayInputValue, earliestReportDay } from './collections-report-view';

/**
 * The "Print collections" dialog of the Payments register (change request 2026-10-13, item 2): a period of whole days
 * (default today), the scope (own payments, or the whole branch when the member holds `canViewBranchCollections`), the
 * grouping (branch / cashier / payment method, default method), a compact table of the loaded report and the print
 * action. The earliest day the member may report (`reportDaysLimit`) bounds the inputs; the server decides again.
 */
@Component({
  selector: 'app-billing-collections-report-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, IconComponent],
  template: `
    <app-dialog [open]="open()" (openChange)="open.set($event)" [title]="i18n.t('billing.collections.title')" size="lg">
      <form [formGroup]="form" (ngSubmit)="load()" novalidate>
        <div class="form-grid">
          <app-form-field [label]="i18n.t('billing.filter.from')" [hint]="fromHint()">
            <input type="date" formControlName="from" [attr.min]="earliest()" [attr.max]="today" />
          </app-form-field>
          <app-form-field [label]="i18n.t('billing.filter.to')">
            <input type="date" formControlName="to" [attr.min]="earliest()" />
          </app-form-field>
          <fieldset class="choice">
            <legend>{{ i18n.t('billing.collections.scope') }}</legend>
            <label class="check"><input type="radio" value="User" formControlName="scope" /><span>{{ i18n.t('billing.collections.scopeUser') }}</span></label>
            @if (canViewBranch()) {
              <label class="check"><input type="radio" value="Branch" formControlName="scope" /><span>{{ i18n.t('billing.collections.scopeBranch') }}</span></label>
            }
          </fieldset>
          <app-form-field [label]="i18n.t('billing.collections.groupBy')">
            <select formControlName="groupBy">
              @for (grouping of groupings; track grouping) {
                <option [value]="grouping">{{ i18n.t('billing.collections.group' + grouping) }}</option>
              }
            </select>
          </app-form-field>
        </div>
        <p class="muted small">{{ i18n.t('billing.collections.hint') }}</p>
        @if (periodProblem(); as text) {
          <p class="problem" role="alert">{{ text }}</p>
        }
        @if (problem(); as problem) {
          <p class="problem" role="alert">{{ problemMessage(problem) }}</p>
        }
      </form>

      @if (report(); as doc) {
        <table class="report">
          <caption>{{ i18n.t('billing.collections.amounts') }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('billing.collections.group' + doc.groupBy) }}</th>
              <th scope="col" class="num">{{ i18n.t('billing.collections.payments') }}</th>
              <th scope="col" class="num">{{ i18n.t('billing.collections.refunds') }}</th>
              <th scope="col" class="num">{{ i18n.t('billing.method.Cash') }}</th>
              <th scope="col" class="num">{{ i18n.t('billing.method.Card') }}</th>
              <th scope="col" class="num">{{ i18n.t('billing.method.Pos') }}</th>
              <th scope="col" class="num">{{ i18n.t('billing.method.Transfer') }}</th>
              <th scope="col" class="num">{{ i18n.t('billing.collections.net') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of doc.rows; track row.key) {
              <tr>
                <th scope="row">{{ label(row, doc.groupBy) }}</th>
                <td class="num">{{ money(row.totalPayments) }} <span class="muted">({{ count(row.paymentCount) }})</span></td>
                <td class="num">{{ money(row.totalRefunds) }} <span class="muted">({{ count(row.refundCount) }})</span></td>
                <td class="num">{{ money(row.cash.net) }}</td>
                <td class="num">{{ money(row.card.net) }}</td>
                <td class="num">{{ money(row.pos.net) }}</td>
                <td class="num">{{ money(row.transfer.net) }}</td>
                <td class="num">{{ money(row.net) }}</td>
              </tr>
            } @empty {
              <tr><td colspan="8" class="muted">{{ i18n.t('billing.collections.none') }}</td></tr>
            }
          </tbody>
          <tfoot>
            <tr class="totals">
              <th scope="row">{{ i18n.t('billing.doc.total') }}</th>
              <td class="num">{{ money(doc.totals.totalPayments) }} <span class="muted">({{ count(doc.totals.paymentCount) }})</span></td>
              <td class="num">{{ money(doc.totals.totalRefunds) }} <span class="muted">({{ count(doc.totals.refundCount) }})</span></td>
              <td class="num">{{ money(doc.totals.cash.net) }}</td>
              <td class="num">{{ money(doc.totals.card.net) }}</td>
              <td class="num">{{ money(doc.totals.pos.net) }}</td>
              <td class="num">{{ money(doc.totals.transfer.net) }}</td>
              <td class="num">{{ money(doc.totals.net) }}</td>
            </tr>
          </tfoot>
        </table>
        <p class="muted small">{{ doc.currency }} · {{ i18n.t('billing.collections.equation') }}</p>
      }

      <ng-container dialogFooter>
        <button type="button" class="btn ghost" (click)="open.set(false)">{{ i18n.t('shared.dialog.close') }}</button>
        <button type="button" class="btn" (click)="load()" [disabled]="!ready()"><app-icon name="search" [size]="15" />{{ i18n.t('billing.collections.load') }}</button>
        <button type="button" class="btn primary" (click)="print()" [disabled]="!ready()"><app-icon name="printer" [size]="15" />{{ i18n.t('billing.collections.print') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .choice {
      display: grid;
      gap: 6px;
      margin: 0;
      padding: 0;
      border: 0;
    }

    .choice legend {
      font-size: 12px;
      font-weight: 600;
      padding: 0;
      margin-bottom: 4px;
    }

    .report {
      width: 100%;
      margin-top: var(--sp-3);
      border-collapse: collapse;
      font-size: 13px;
    }

    .report th,
    .report td {
      padding: 6px 8px;
      border-bottom: 1px solid var(--line);
      text-align: start;
    }

    .report .num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    .report tfoot .totals th,
    .report tfoot .totals td {
      font-weight: 700;
      border-top: 2px solid var(--line);
      border-bottom: 0;
    }

    .small {
      font-size: 12px;
      margin: var(--sp-2) 0 0;
    }

    .problem {
      margin: var(--sp-2) 0 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CollectionsReportDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  /** The member's rights; null while they load (the branch option stays hidden until they arrive). */
  readonly privileges = input<CollectionsReportPrivileges | null>(null);
  readonly report = input<CollectionsReportDocument | null>(null);
  readonly pending = input(false);
  readonly problem = input<ProblemDetails | null>(null);

  readonly loadRequested = output<CollectionsReportRequest>();
  readonly printRequested = output<CollectionsReportRequest>();

  protected readonly groupings = COLLECTIONS_REPORT_GROUPINGS;
  protected readonly today = dayInputValue(new Date());

  protected readonly form = new FormGroup({
    from: new FormControl(this.today, { nonNullable: true }),
    to: new FormControl(this.today, { nonNullable: true }),
    scope: new FormControl<CollectionsReportScope>('User', { nonNullable: true }),
    groupBy: new FormControl<CollectionsReportGrouping>('Method', { nonNullable: true }),
  });

  /** The form's values as a signal (valueChanges through toSignal, never a subscribe in the component). */
  private readonly values = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), { initialValue: this.form.getRawValue() });

  protected readonly canViewBranch = computed(() => this.privileges()?.canViewBranchCollections === true);

  protected readonly earliest = computed(() => earliestReportDay(this.privileges()?.reportDaysLimit ?? null));

  protected readonly fromHint = computed(() => {
    const earliest = this.earliest();
    if (earliest === null) {
      return null;
    }

    return earliest === this.today ? this.i18n.t('billing.collections.todayOnly') : this.i18n.tf('billing.collections.earliestHint', { day: earliest });
  });

  protected readonly periodProblem = computed<string | null>(() => {
    const { from, to } = this.values();
    if (from === '' || to === '') {
      return null;
    }

    if (to < from) {
      return this.i18n.t('billing.collections.invalidPeriod');
    }

    const earliest = this.earliest();
    return earliest !== null && from < earliest ? this.i18n.t('billing.collections.beyondLimit') : null;
  });

  protected readonly ready = computed(() => {
    const { from, to } = this.values();
    return !this.pending() && this.privileges() !== null && from !== '' && to !== '' && this.periodProblem() === null;
  });

  constructor() {
    // A member without the branch right never keeps "Branch" selected (the rights may arrive after the choice).
    effect(() => {
      const canViewBranch = this.canViewBranch();
      untracked(() => {
        if (!canViewBranch && this.form.controls.scope.value === 'Branch') {
          this.form.controls.scope.setValue('User');
        }
      });
    });

    // Opening resets to today's own collections by method (the default of the change request).
    effect(() => {
      const isOpen = this.open();
      untracked(() => {
        if (isOpen) {
          this.form.reset({ from: this.today, to: this.today, scope: 'User', groupBy: 'Method' });
        }
      });
    });
  }

  protected load(): void {
    if (this.ready()) {
      this.loadRequested.emit(this.request());
    }
  }

  protected print(): void {
    if (this.ready()) {
      this.printRequested.emit(this.request());
    }
  }

  protected label(row: CollectionsReportRow, groupBy: CollectionsReportGrouping): string {
    return collectionsRowLabel(this.i18n, row, groupBy);
  }

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected count(value: number): string {
    return formatCount(value, this.i18n.lang());
  }

  protected problemMessage(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'billing', problem);
  }

  private request(): CollectionsReportRequest {
    const { from, to, scope, groupBy } = this.form.getRawValue();
    return { fromDay: from, toDay: to, scope, groupBy };
  }
}

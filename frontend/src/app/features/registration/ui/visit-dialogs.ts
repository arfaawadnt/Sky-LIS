import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, resource, signal, untracked } from '@angular/core';

import type { CancelLinesRequest, VisitLineItem } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { RegistrationApi } from '../data/registration.api';
import { dateTime, money, pick } from '../data/registration-format';
import { RegistrationPrinter } from '../state/registration-printer';

/**
 * Cancel services of a visit with a reason (CP-10 row action, CP-26 rules; BR-15): the live services of the visit
 * (a package cancels with its members), a cancellation reason from the catalogue and/or a note. Emits the request;
 * the page sends it and shows the server's refusal (no Cancel right REG-061, verified results REG-106).
 */
@Component({
  selector: 'app-reg-cancel-services-dialog',
  imports: [DialogComponent, BadgeComponent],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.tf('registration.cancel.title', { accession: accession() })" size="lg" [danger]="true">
      @if (detail.isLoading()) {
        <p class="muted small" role="status">{{ i18n.t('registration.loading') }}</p>
      } @else if (detail.error()) {
        <p class="problem" role="alert">{{ i18n.t('registration.visitLoadError') }}</p>
      } @else {
        <p class="warn-strip">{{ i18n.t('registration.cancel.warning') }}</p>
        <ul class="lines">
          @for (line of lines(); track line.id) {
            <li>
              <label class="check" [class.disabled]="line.status === 'Cancelled'">
                <input type="checkbox" [disabled]="line.status === 'Cancelled'" [checked]="selected().has(line.id)" (change)="toggle(line.id, $event)" />
                <span class="mono">{{ line.code }}</span>
                <span>{{ name(line.nameEn, line.nameAr) }}</span>
                <app-badge [status]="line.status">{{ i18n.t('registration.lineStatus.' + line.status) }}</app-badge>
                <span class="num small">{{ amount(line.patientShare) }} / {{ amount(line.payerShare) }}</span>
              </label>
              @if (line.status === 'Cancelled') {
                <div class="small muted">{{ line.cancellationReason }} · {{ line.cancelledByName ?? '' }} · {{ when(line.cancelledAtUtc) }}</div>
              }
            </li>
          }
        </ul>
        <div class="form-grid">
          <label class="field">
            <span>{{ i18n.t('registration.cancel.reason') }}</span>
            <select (change)="reasonId.set(value($event))">
              <option value="">{{ i18n.t('registration.cancel.chooseReason') }}</option>
              @for (reason of reasons.value() ?? []; track reason.id) {
                <option [value]="reason.id" [selected]="reason.id === reasonId()">{{ name(reason.nameEn, reason.nameAr) }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span>{{ i18n.t('registration.cancel.note') }}</span>
            <input type="text" maxlength="500" [value]="note()" (input)="note.set(value($event))" />
          </label>
        </div>
        @if (problem(); as text) {
          <p class="problem" role="alert">{{ text }}</p>
        }
      }
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('registration.close') }}</button>
        <button type="button" class="btn danger" [disabled]="!canSubmit() || pending()" (click)="submit()">{{ i18n.t('registration.cancel.submit') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .lines {
      list-style: none;
      margin: var(--sp-3) 0;
      padding: 0;
      display: grid;
      gap: var(--sp-2);
    }

    .lines .check {
      display: flex;
      gap: var(--sp-2);
      align-items: center;
      flex-wrap: wrap;
    }

    .lines .disabled {
      color: var(--muted);
      text-decoration: line-through;
    }

    .num {
      margin-inline-start: auto;
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CancelServicesDialogComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(RegistrationApi);

  readonly open = model(false);
  readonly visitId = input<string | null>(null);
  readonly accession = input('');
  readonly pending = input(false);
  readonly problem = input<string | null>(null);
  /** Line ids ticked when the dialog opens (Accession details opens it from a row, CP-10b); the user may still change the set. */
  readonly preselect = input<readonly string[]>([]);
  readonly submitted = output<{ visitId: string; request: CancelLinesRequest }>();

  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly reasonId = signal('');
  protected readonly note = signal('');

  protected readonly detail = resource({
    params: () => (this.open() ? (this.visitId() ?? undefined) : undefined),
    loader: ({ params, abortSignal }) => this.api.get(params, abortSignal),
  });

  protected readonly reasons = resource({
    params: () => (this.open() ? true : undefined),
    loader: ({ abortSignal }) => this.api.reasons('Cancellation', abortSignal),
  });

  /** Top-level lines only: a package cancels with its members (INV-REG-007), a formula line with its operand lines (LIS-CR-015). */
  protected readonly lines = computed<readonly VisitLineItem[]>(() => (this.detail.hasValue() ? this.detail.value().lines.filter((line) => line.kind !== 'PackageMember' && line.kind !== 'FormulaOperand') : []));

  protected readonly canSubmit = computed(() => this.selected().size > 0 && (this.reasonId() !== '' || this.note().trim() !== ''));

  constructor() {
    effect(() => {
      const isOpen = this.open();
      untracked(() => {
        if (isOpen) {
          this.selected.set(new Set(this.preselect()));
          this.reasonId.set('');
          this.note.set('');
        }
      });
    });
  }

  protected toggle(lineId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selected.update((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(lineId);
      } else {
        next.delete(lineId);
      }

      return next;
    });
  }

  protected submit(): void {
    const visitId = this.visitId();
    if (visitId === null || !this.canSubmit()) {
      return;
    }

    this.submitted.emit({
      visitId,
      request: { lineIds: [...this.selected()], reasonId: this.reasonId() === '' ? null : this.reasonId(), reason: this.note().trim() === '' ? null : this.note().trim() },
    });
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
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

/** The samples of a visit (CP-10 "samples" row action, a dialog in Release 1): number, unit, type, status, times; print one or all labels. */
@Component({
  selector: 'app-reg-visit-samples-dialog',
  imports: [DialogComponent, BadgeComponent, IconComponent],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.tf('registration.samples.title', { accession: accession() })" size="lg">
      @if (samples.isLoading()) {
        <p class="muted small" role="status">{{ i18n.t('registration.loading') }}</p>
      } @else if ((samples.value() ?? []).length === 0) {
        <p class="muted small">{{ i18n.t('registration.samples.empty') }}</p>
      } @else {
        <table class="samples">
          <thead>
            <tr>
              <th>{{ i18n.t('registration.column.sample') }}</th>
              <th>{{ i18n.t('registration.column.unit') }}</th>
              <th>{{ i18n.t('registration.column.sampleType') }}</th>
              <th>{{ i18n.t('registration.column.status') }}</th>
              <th>{{ i18n.t('registration.column.collected') }}</th>
              <th>{{ i18n.t('registration.column.received') }}</th>
              <th><span class="visually-hidden">{{ i18n.t('registration.column.actions') }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (sample of samples.value() ?? []; track sample.id) {
              <tr>
                <td class="mono">{{ sample.sampleNumber }}</td>
                <td>{{ sample.unitCode ?? '' }}</td>
                <td>
                  {{ name(sample.sampleTypeNameEn, sample.sampleTypeNameAr) }}
                  @if (sample.collectedOutside) {
                    <app-badge tone="info">{{ i18n.t('registration.sampleFlags.outside') }}</app-badge>
                  }
                </td>
                <td><app-badge [status]="sample.status" /></td>
                <td class="small">{{ when(sample.collectedAtUtc) }}</td>
                <td class="small">{{ when(sample.receivedAtUtc) }}</td>
                <td>
                  <button type="button" class="btn sm" (click)="printOne(sample.tubeId)"><app-icon name="barcode" [size]="14" />{{ i18n.t('registration.samples.printLabel') }}</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="printAll()"><app-icon name="printer" [size]="15" />{{ i18n.t('registration.samples.printAll') }}</button>
        <button type="button" class="btn primary" (click)="open.set(false)">{{ i18n.t('registration.close') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .samples {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-sm);
    }

    .samples th,
    .samples td {
      padding: var(--sp-2);
      border-block-end: 1px solid var(--line-soft);
      text-align: start;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisitSamplesDialogComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(RegistrationApi);
  private readonly printer = inject(RegistrationPrinter);
  private readonly toast = inject(ToastService);

  readonly open = model(false);
  readonly visitId = input<string | null>(null);
  readonly accession = input('');

  protected readonly samples = resource({
    params: () => (this.open() ? (this.visitId() ?? undefined) : undefined),
    loader: ({ params, abortSignal }) => this.api.samples(params, abortSignal),
  });

  protected async printOne(tubeId: string): Promise<void> {
    const visitId = this.visitId();
    if (visitId !== null) {
      await this.printer.labels(visitId, tubeId).catch(() => this.toast.error(this.i18n.t('registration.printError')));
    }
  }

  protected async printAll(): Promise<void> {
    const visitId = this.visitId();
    if (visitId !== null) {
      await this.printer.labels(visitId).catch(() => this.toast.error(this.i18n.t('registration.printError')));
    }
  }

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected when(utc: string | null): string {
    return dateTime(utc, this.i18n.lang());
  }
}

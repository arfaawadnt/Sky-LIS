import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { ConversionDto } from '../../../core/api/integration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

type ConversionRow = FormGroup<{ fromValue: FormControl<string>; toValue: FormControl<string> }>;

export const MAX_CONVERSIONS = 50;

/**
 * The conversion table of a mapping (CP-59 "conversion table"; LDM "Result…"): source values — exact text, or a
 * comparison such as <10 or >=50 on the converted number — and the text each becomes; the first matching row wins.
 * Enter in the last row adds a row; Ctrl+S saves.
 */
@Component({
  selector: 'app-integration-conversions-dialog',
  imports: [ReactiveFormsModule, DialogComponent, IconComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('integration.conversions.title')" size="lg">
      <p class="muted small">{{ i18n.t('integration.conversions.hint') }}</p>
      <form [formGroup]="form" appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate>
        <table class="grid">
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">{{ i18n.t('integration.conversions.from') }}</th>
              <th scope="col">{{ i18n.t('integration.conversions.to') }}</th>
              <th scope="col"><span class="visually-hidden">{{ i18n.t('integration.actions') }}</span></th>
            </tr>
          </thead>
          <tbody formArrayName="rows">
            @for (row of form.controls.rows.controls; track row; let index = $index; let last = $last) {
              <tr [formGroupName]="index">
                <td class="num">{{ index + 1 }}</td>
                <td><input class="inp mono" formControlName="fromValue" maxlength="100" [attr.aria-label]="i18n.t('integration.conversions.from')" placeholder="<10" /></td>
                <td><input class="inp" formControlName="toValue" maxlength="100" [attr.aria-label]="i18n.t('integration.conversions.to')" (keydown.enter)="onEnter($event, last)" /></td>
                <td><button type="button" class="icon-btn" (click)="remove(index)" [attr.aria-label]="i18n.t('integration.remove')"><app-icon name="x" [size]="14" /></button></td>
              </tr>
            }
          </tbody>
        </table>
        @if (form.controls.rows.length === 0) {
          <p class="muted">{{ i18n.t('integration.conversions.empty') }}</p>
        }
        <button type="button" class="btn sm" (click)="add()" [disabled]="form.controls.rows.length >= max"><app-icon name="plus" [size]="14" />{{ i18n.t('integration.conversions.add') }}</button>
      </form>
      <button dialogFooter type="button" class="btn ghost" (click)="open.set(false)">{{ i18n.t('integration.cancel') }}</button>
      <button dialogFooter type="button" class="btn primary" [disabled]="pending()" (click)="submit()">{{ i18n.t('integration.save') }}</button>
    </app-dialog>
  `,
  styles: `
    .grid { width: 100%; border-collapse: collapse; margin-block-end: var(--sp-3); }
    .grid th, .grid td { padding: var(--sp-1) var(--sp-2); text-align: start; }
    .grid .num { width: 2.5rem; color: var(--ink-3); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConversionsDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly conversions = input<readonly ConversionDto[]>([]);
  readonly pending = input(false);
  readonly saved = output<readonly ConversionDto[]>();

  protected readonly max = MAX_CONVERSIONS;
  protected readonly form = new FormGroup({ rows: new FormArray<ConversionRow>([]) });

  constructor() {
    effect(() => {
      if (this.open()) {
        const rows = this.conversions();
        untracked(() => this.reset(rows));
      }
    });
  }

  protected add(value: ConversionDto = { fromValue: '', toValue: '' }): void {
    if (this.form.controls.rows.length >= this.max) {
      return;
    }

    this.form.controls.rows.push(
      new FormGroup({
        fromValue: new FormControl(value.fromValue, { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
        toValue: new FormControl(value.toValue, { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
      }),
    );
  }

  protected remove(index: number): void {
    this.form.controls.rows.removeAt(index);
  }

  protected onEnter(event: Event, last: boolean): void {
    event.preventDefault();
    if (last) {
      this.add();
    }
  }

  protected submit(): void {
    if (!markAllTouched(this.form)) {
      return;
    }

    this.saved.emit(this.form.controls.rows.getRawValue().map((row) => ({ fromValue: row.fromValue.trim(), toValue: row.toValue.trim() })));
  }

  private reset(rows: readonly ConversionDto[]): void {
    this.form.controls.rows.clear();
    for (const row of rows) {
      this.add(row);
    }
  }
}

import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import type { SampleConditionItem, SampleTypeDetail } from '../../../core/api/catalogue-a.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator, requiredText } from '../../../shared/forms/validators';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

type ConditionRow = FormGroup<{
  id: FormControl<string | null>;
  code: FormControl<string>;
  nameEn: FormControl<string>;
  nameAr: FormControl<string>;
  isActive: FormControl<boolean>;
}>;

/**
 * The conditions of a sample type (CP-43: fasting, random, refrigerated…) in a drawer next to the sample-type
 * list. Existing conditions keep their code (it is printed on labels and referenced by services and tests); a
 * condition is deactivated, never removed (BR-15). Emits the full list; the page saves it.
 */
@Component({
  selector: 'app-sample-conditions-drawer',
  imports: [ReactiveFormsModule, DrawerComponent, IconComponent, SaveShortcutDirective, UppercaseDirective],
  template: `
    <app-drawer [(open)]="open" [title]="title()" size="lg" (closed)="closed.emit()">
      <form [formGroup]="form" appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate>
        <p class="muted small">{{ i18n.t('catalogue-a.sampleTypes.conditionsHint') }}</p>
        @if (problem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        <table class="grid" formArrayName="rows">
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('catalogue-a.sampleTypes.condition.code') }}</th>
              <th scope="col">{{ i18n.t('catalogue-a.sampleTypes.condition.nameEn') }}</th>
              <th scope="col">{{ i18n.t('catalogue-a.sampleTypes.condition.nameAr') }}</th>
              <th scope="col">{{ i18n.t('catalogue-a.sampleTypes.condition.active') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows.controls; track row; let i = $index) {
              <tr [formGroupName]="i">
                <td>
                  <input class="inp sm mono" formControlName="code" appUppercase maxlength="20" [attr.aria-label]="i18n.t('catalogue-a.sampleTypes.condition.code')" [class.invalid]="row.controls.code.invalid && row.controls.code.touched" />
                </td>
                <td><input class="inp sm" formControlName="nameEn" dir="ltr" maxlength="200" [attr.aria-label]="i18n.t('catalogue-a.sampleTypes.condition.nameEn')" [class.invalid]="row.controls.nameEn.invalid && row.controls.nameEn.touched" /></td>
                <td><input class="inp sm" formControlName="nameAr" dir="rtl" lang="ar" maxlength="200" [attr.aria-label]="i18n.t('catalogue-a.sampleTypes.condition.nameAr')" [class.invalid]="row.controls.nameAr.invalid && row.controls.nameAr.touched" /></td>
                <td class="center">
                  @if (row.controls.id.value === null) {
                    <button type="button" class="row-btn danger" (click)="remove(i)" [attr.aria-label]="i18n.t('catalogue-a.remove')" [title]="i18n.t('catalogue-a.remove')"><app-icon name="x" [size]="14" /></button>
                  } @else {
                    <input type="checkbox" formControlName="isActive" [attr.aria-label]="i18n.t('catalogue-a.sampleTypes.condition.active')" />
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="4" class="muted">{{ i18n.t('catalogue-a.sampleTypes.noConditions') }}</td></tr>
            }
          </tbody>
        </table>
        <button type="button" class="btn sm soft add" (click)="add()"><app-icon name="plus" [size]="14" />{{ i18n.t('catalogue-a.sampleTypes.addCondition') }}</button>
      </form>
      <ng-container drawerFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('catalogue-a.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="saving()" (click)="submit()">{{ saving() ? i18n.t('catalogue-a.saving') : i18n.t('catalogue-a.save') }}</button>
      </ng-container>
    </app-drawer>
  `,
  styles: `
    .grid { width: 100%; border-collapse: collapse; margin-block: var(--sp-3); }
    .grid th { text-align: start; font-size: var(--fs-sm); color: var(--ink-2); padding: 4px 6px; border-bottom: 1px solid var(--line); }
    .grid td { padding: 4px 6px; vertical-align: middle; }
    .grid td.center { text-align: center; }
    .add { margin-top: var(--sp-2); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SampleConditionsDrawerComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly detail = input<SampleTypeDetail | null>(null);
  readonly saving = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly save = output<readonly SampleConditionItem[]>();
  readonly closed = output<void>();

  protected readonly form = new FormGroup({ rows: new FormArray<ConditionRow>([]) });
  protected readonly rows = this.form.controls.rows;

  protected readonly title = computed(() => {
    const detail = this.detail();
    return detail === null
      ? this.i18n.t('catalogue-a.sampleTypes.conditions')
      : `${this.i18n.t('catalogue-a.sampleTypes.conditions')} · ${detail.code} · ${localizedPick({ en: detail.nameEn, ar: detail.nameAr }, this.i18n.lang())}`;
  });

  constructor() {
    effect(() => {
      const detail = this.detail();
      untracked(() => this.reset(detail));
    });
  }

  protected add(): void {
    this.rows.push(row(null));
  }

  protected remove(index: number): void {
    this.rows.removeAt(index);
  }

  protected submit(): void {
    if (this.saving() || !markAllTouched(this.form)) {
      return;
    }

    this.save.emit(
      this.rows.getRawValue().map((value) => ({ id: value.id, code: value.code.trim(), nameEn: value.nameEn.trim(), nameAr: value.nameAr.trim(), isActive: value.isActive })),
    );
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'catalogue-a', problem);
  }

  private reset(detail: SampleTypeDetail | null): void {
    this.rows.clear();
    for (const condition of detail?.conditions ?? []) {
      this.rows.push(row(condition));
    }
  }
}

function row(condition: { id: string; code: string; nameEn: string; nameAr: string; isActive: boolean } | null): ConditionRow {
  const group: ConditionRow = new FormGroup({
    id: new FormControl<string | null>(condition?.id ?? null),
    code: new FormControl(condition?.code ?? '', { nonNullable: true, validators: [requiredText, codeValidator] }),
    nameEn: new FormControl(condition?.nameEn ?? '', { nonNullable: true, validators: [requiredText] }),
    nameAr: new FormControl(condition?.nameAr ?? '', { nonNullable: true, validators: [requiredText] }),
    isActive: new FormControl(condition?.isActive ?? true, { nonNullable: true }),
  });
  if (condition !== null) {
    group.controls.code.disable();   // immutable once created
  }

  return group;
}

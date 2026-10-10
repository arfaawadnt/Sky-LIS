import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { CultureEntryDto, MicrobiologyLookups, ResultRowView } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { isSigned, pickName } from './result-format';

/** What the culture dialog produces: the culture value and, for a signed result, the amendment reason (T6). */
export interface CultureRequest {
  readonly row: ResultRowView;
  readonly culture: CultureEntryDto;
  readonly reason: string | null;
}

type SensitivityGroup = FormGroup<{ antibioticId: FormControl<string>; interpretation: FormControl<string>; mic: FormControl<string> }>;

type IsolateGroup = FormGroup<{ organismId: FormControl<string>; quantity: FormControl<string>; comment: FormControl<string>; sensitivities: FormArray<SensitivityGroup> }>;

/**
 * Culture entry (CP-19, LDM §3.5/§5.3; intake T2 RES-008): "No growth", or one isolate or more, each an organism
 * with quantity, comment and the antibiotic sensitivities (interpretation from the sensitivity values, optional
 * MIC). A signed result is amended, so the reason is asked (RES-019). Ctrl+S saves, Esc closes.
 */
@Component({
  selector: 'app-culture-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, IconComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('results.culture.title')" size="lg">
      @if (row(); as current) {
        <form [formGroup]="form" appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate>
          <p class="muted small">{{ testName(current) }} · {{ current.sampleNumber ?? '' }}</p>
          <label class="check">
            <input type="checkbox" formControlName="noGrowth" />
            {{ i18n.t('results.culture.noGrowth') }}
          </label>
          @if (!form.controls.noGrowth.value) {
            <div formArrayName="isolates" class="isolates">
              @for (isolate of form.controls.isolates.controls; track isolate; let i = $index) {
                <fieldset [formGroupName]="i" class="isolate">
                  <legend>{{ i18n.tf('results.culture.isolate', { index: i + 1 }) }}</legend>
                  <div class="row">
                    <app-form-field [label]="i18n.t('results.culture.organism')" [required]="true">
                      <select formControlName="organismId">
                        <option value="">{{ i18n.t('shared.form.choose') }}</option>
                        @for (organism of lookups().organisms; track organism.id) {
                          <option [value]="organism.id">{{ organism.code }} · {{ name(organism.nameEn, organism.nameAr) }}</option>
                        }
                      </select>
                    </app-form-field>
                    <app-form-field [label]="i18n.t('results.culture.quantity')">
                      <input type="text" formControlName="quantity" maxlength="100" autocomplete="off" />
                    </app-form-field>
                    <button type="button" class="btn sm ghost remove" (click)="removeIsolate(i)" [attr.aria-label]="i18n.t('results.culture.removeIsolate')"><app-icon name="x" [size]="14" /></button>
                  </div>
                  <app-form-field [label]="i18n.t('results.culture.comment')">
                    <input type="text" formControlName="comment" maxlength="500" autocomplete="off" />
                  </app-form-field>
                  <table class="sensitivities" formArrayName="sensitivities">
                    <caption class="visually-hidden">{{ i18n.t('results.culture.sensitivities') }}</caption>
                    <thead>
                      <tr>
                        <th scope="col">{{ i18n.t('results.culture.antibiotic') }}</th>
                        <th scope="col">{{ i18n.t('results.culture.interpretation') }}</th>
                        <th scope="col">{{ i18n.t('results.culture.mic') }}</th>
                        <th scope="col"><span class="visually-hidden">{{ i18n.t('results.grid.actions') }}</span></th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (sensitivity of isolate.controls.sensitivities.controls; track sensitivity; let j = $index) {
                        <tr [formGroupName]="j">
                          <td>
                            <select class="inp sm" formControlName="antibioticId" [attr.aria-label]="i18n.t('results.culture.antibiotic')">
                              <option value="">{{ i18n.t('shared.form.choose') }}</option>
                              @for (antibiotic of lookups().antibiotics; track antibiotic.id) {
                                <option [value]="antibiotic.id">{{ antibiotic.abbreviation }} · {{ name(antibiotic.nameEn, antibiotic.nameAr) }}</option>
                              }
                            </select>
                          </td>
                          <td>
                            <select class="inp sm" formControlName="interpretation" [attr.aria-label]="i18n.t('results.culture.interpretation')">
                              <option value="">{{ i18n.t('shared.form.choose') }}</option>
                              @for (value of lookups().sensitivities; track value.id) {
                                <option [value]="value.code">{{ value.code }} · {{ name(value.nameEn, value.nameAr) }}</option>
                              }
                            </select>
                          </td>
                          <td><input class="inp sm" type="text" formControlName="mic" maxlength="50" autocomplete="off" [attr.aria-label]="i18n.t('results.culture.mic')" /></td>
                          <td><button type="button" class="btn sm ghost" (click)="removeSensitivity(isolate, j)" [attr.aria-label]="i18n.t('results.culture.removeSensitivity')"><app-icon name="x" [size]="14" /></button></td>
                        </tr>
                      }
                    </tbody>
                  </table>
                  <button type="button" class="btn sm soft" (click)="addSensitivity(isolate)"><app-icon name="plus" [size]="14" />{{ i18n.t('results.culture.addSensitivity') }}</button>
                </fieldset>
              }
            </div>
            <button type="button" class="btn sm" (click)="addIsolate()"><app-icon name="plus" [size]="14" />{{ i18n.t('results.culture.addIsolate') }}</button>
            @if (form.touched && form.errors?.['isolates']) {
              <p class="problem" role="alert">{{ i18n.t('results.error.RES-008') }}</p>
            }
          }
          @if (needsReason()) {
            <app-form-field [label]="i18n.t('results.amend.reason')" [required]="true">
              <textarea rows="2" formControlName="reason" maxlength="1000"></textarea>
            </app-form-field>
          }
        </form>
      }
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('results.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="busy()" (click)="submit()">{{ i18n.t('results.save') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .check { display: inline-flex; align-items: center; gap: 8px; margin: 4px 0 12px; font-size: var(--fs-sm); }
    .isolates { display: grid; gap: var(--sp-3); margin-bottom: var(--sp-3); }
    .isolate { border: 1px solid var(--line); border-radius: var(--r-sm); padding: var(--sp-3); margin: 0; }
    .isolate legend { font-weight: 600; font-size: var(--fs-sm); padding: 0 4px; }
    .row { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr) auto; gap: var(--sp-3); align-items: start; }
    .remove { margin-top: 22px; }
    .sensitivities { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); margin: var(--sp-2) 0; }
    .sensitivities th, .sensitivities td { padding: 4px; text-align: start; }
    .sensitivities .inp { width: 100%; }
    @media (max-width: 720px) { .row { grid-template-columns: 1fr; } .remove { margin-top: 0; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CultureDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly row = input<ResultRowView | null>(null);
  readonly lookups = input<MicrobiologyLookups>({ organisms: [], antibiotics: [], sensitivities: [] });
  readonly busy = input(false);

  readonly submitted = output<CultureRequest>();

  protected readonly needsReason = computed(() => {
    const row = this.row();
    return row !== null && isSigned(row.status);
  });

  protected readonly form = new FormGroup(
    {
      noGrowth: new FormControl(false, { nonNullable: true }),
      isolates: new FormArray<IsolateGroup>([]),
      reason: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
    },
    { validators: [(group) => (group.get('noGrowth')?.value === true || (group.get('isolates') as FormArray | null)?.length ? null : { isolates: true })] },
  );

  constructor() {
    effect(() => {
      const row = this.row();
      if (this.open() && row) {
        untracked(() => this.load(row));
      }
    });
  }

  protected addIsolate(): void {
    this.form.controls.isolates.push(this.isolateGroup());
  }

  protected removeIsolate(index: number): void {
    this.form.controls.isolates.removeAt(index);
  }

  protected addSensitivity(isolate: IsolateGroup): void {
    isolate.controls.sensitivities.push(this.sensitivityGroup());
  }

  protected removeSensitivity(isolate: IsolateGroup, index: number): void {
    isolate.controls.sensitivities.removeAt(index);
  }

  protected submit(): void {
    const row = this.row();
    if (!row) {
      return;
    }

    if (this.needsReason()) {
      this.form.controls.reason.setValidators([requiredText, Validators.maxLength(1000)]);
    } else {
      this.form.controls.reason.setValidators([Validators.maxLength(1000)]);
    }

    if (!markAllTouched(this.form)) {
      return;
    }

    const value = this.form.getRawValue();
    const culture: CultureEntryDto = {
      noGrowth: value.noGrowth,
      isolates: value.noGrowth
        ? []
        : value.isolates.map((isolate) => ({
            organismId: isolate.organismId,
            quantity: isolate.quantity.trim() || null,
            comment: isolate.comment.trim() || null,
            sensitivities: isolate.sensitivities
              .filter((sensitivity) => sensitivity.antibioticId !== '' && sensitivity.interpretation !== '')
              .map((sensitivity) => ({ antibioticId: sensitivity.antibioticId, interpretation: sensitivity.interpretation, mic: sensitivity.mic.trim() || null })),
          })),
    };
    this.submitted.emit({ row, culture, reason: this.needsReason() ? value.reason.trim() : null });
  }

  protected testName(row: ResultRowView): string {
    return pickName(row.reportName ?? row.testNameEn, row.testNameAr, this.i18n.lang());
  }

  protected name(en: string, ar: string): string {
    return pickName(en, ar, this.i18n.lang());
  }

  private load(row: ResultRowView): void {
    this.form.controls.isolates.clear();
    const culture = row.culture;
    this.form.reset({ noGrowth: culture?.noGrowth ?? false, reason: '' });
    for (const isolate of culture?.isolates ?? []) {
      const group = this.isolateGroup();
      group.patchValue({ organismId: isolate.organismId, quantity: isolate.quantity ?? '', comment: isolate.comment ?? '' });
      for (const sensitivity of isolate.sensitivities) {
        const sensitivityGroup = this.sensitivityGroup();
        sensitivityGroup.patchValue({ antibioticId: sensitivity.antibioticId, interpretation: sensitivity.interpretation, mic: sensitivity.mic ?? '' });
        group.controls.sensitivities.push(sensitivityGroup);
      }

      this.form.controls.isolates.push(group);
    }
  }

  private isolateGroup(): IsolateGroup {
    return new FormGroup({
      organismId: new FormControl('', { nonNullable: true, validators: [requiredText] }),
      quantity: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
      comment: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
      sensitivities: new FormArray<SensitivityGroup>([]),
    });
  }

  private sensitivityGroup(): SensitivityGroup {
    return new FormGroup({
      antibioticId: new FormControl('', { nonNullable: true }),
      interpretation: new FormControl('', { nonNullable: true }),
      mic: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(50)] }),
    });
  }
}

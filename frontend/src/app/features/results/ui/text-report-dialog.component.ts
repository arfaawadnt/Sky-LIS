import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { FormattedParagraphDto, ResultRowView } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { isSigned, pickName } from './result-format';

/** What the text-report dialog produces: the paragraphs and, for a signed result, the amendment reason (T6). */
export interface TextReportRequest {
  readonly row: ResultRowView;
  readonly paragraphs: readonly FormattedParagraphDto[];
  readonly reason: string | null;
}

type ParagraphGroup = FormGroup<{ title: FormControl<string>; text: FormControl<string> }>;

/**
 * Formatted-text entry (CP-19, LDM §3.6 "paragraphs"; intake T2 RES-007): the paragraphs of the test (titles from
 * the catalogue's templates, prefilled with their default text), each with a title and a text; paragraphs can be
 * added or removed. A signed result is amended, so the reason is asked (RES-019). Ctrl+S saves, Esc closes.
 */
@Component({
  selector: 'app-text-report-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, IconComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('results.text.title')" size="lg">
      @if (row(); as current) {
        <form [formGroup]="form" appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate>
          <p class="muted small">{{ testName(current) }}</p>
          <div formArrayName="paragraphs" class="paragraphs">
            @for (paragraph of form.controls.paragraphs.controls; track paragraph; let i = $index) {
              <fieldset [formGroupName]="i" class="paragraph">
                <legend class="visually-hidden">{{ i18n.tf('results.text.paragraph', { index: i + 1 }) }}</legend>
                <div class="row">
                  <app-form-field [label]="i18n.t('results.text.paragraphTitle')">
                    <input type="text" formControlName="title" maxlength="200" autocomplete="off" />
                  </app-form-field>
                  <button type="button" class="btn sm ghost remove" (click)="remove(i)" [attr.aria-label]="i18n.t('results.text.removeParagraph')"><app-icon name="x" [size]="14" /></button>
                </div>
                <app-form-field [label]="i18n.t('results.text.paragraphText')" [required]="true">
                  <textarea rows="5" formControlName="text" maxlength="8000"></textarea>
                </app-form-field>
              </fieldset>
            }
          </div>
          <button type="button" class="btn sm" (click)="add()"><app-icon name="plus" [size]="14" />{{ i18n.t('results.text.addParagraph') }}</button>
          @if (form.touched && form.controls.paragraphs.length === 0) {
            <p class="problem" role="alert">{{ i18n.t('results.error.RES-007') }}</p>
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
    .paragraphs { display: grid; gap: var(--sp-2); margin-bottom: var(--sp-3); }
    .paragraph { border: 1px solid var(--line); border-radius: var(--r-sm); padding: var(--sp-3) var(--sp-3) 0; margin: 0; }
    .row { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: var(--sp-3); align-items: start; }
    .remove { margin-top: 22px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TextReportDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly row = input<ResultRowView | null>(null);
  readonly busy = input(false);

  readonly submitted = output<TextReportRequest>();

  protected readonly needsReason = computed(() => {
    const row = this.row();
    return row !== null && isSigned(row.status);
  });

  protected readonly form = new FormGroup({
    paragraphs: new FormArray<ParagraphGroup>([]),
    reason: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
  });

  constructor() {
    effect(() => {
      const row = this.row();
      if (this.open() && row) {
        untracked(() => this.load(row));
      }
    });
  }

  protected add(): void {
    this.form.controls.paragraphs.push(this.group('', ''));
  }

  protected remove(index: number): void {
    this.form.controls.paragraphs.removeAt(index);
  }

  protected submit(): void {
    const row = this.row();
    if (!row) {
      return;
    }

    this.form.controls.reason.setValidators(this.needsReason() ? [requiredText, Validators.maxLength(1000)] : [Validators.maxLength(1000)]);
    if (!markAllTouched(this.form) || this.form.controls.paragraphs.length === 0) {
      return;
    }

    const value = this.form.getRawValue();
    this.submitted.emit({
      row,
      paragraphs: value.paragraphs.map((paragraph) => ({ title: paragraph.title.trim(), text: paragraph.text.trim() })),
      reason: this.needsReason() ? value.reason.trim() : null,
    });
  }

  protected testName(row: ResultRowView): string {
    return pickName(row.reportName ?? row.testNameEn, row.testNameAr, this.i18n.lang());
  }

  private load(row: ResultRowView): void {
    this.form.controls.paragraphs.clear();
    this.form.reset({ reason: '' });
    const paragraphs = row.paragraphs && row.paragraphs.length > 0 ? row.paragraphs : row.paragraphTemplates.map((template) => ({ title: template.title, text: template.text ?? '' }));
    for (const paragraph of paragraphs) {
      this.form.controls.paragraphs.push(this.group(paragraph.title, paragraph.text));
    }

    if (this.form.controls.paragraphs.length === 0) {
      this.add();
    }
  }

  private group(title: string, text: string): ParagraphGroup {
    return new FormGroup({
      title: new FormControl(title, { nonNullable: true, validators: [Validators.maxLength(200)] }),
      text: new FormControl(text, { nonNullable: true, validators: [requiredText, Validators.maxLength(8000)] }),
    });
  }
}

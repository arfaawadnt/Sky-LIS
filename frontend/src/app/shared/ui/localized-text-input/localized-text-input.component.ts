import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import type { LocalizedTextGroup } from '../../forms/localized-text';
import { FormFieldComponent } from '../form-field/form-field.component';

/**
 * `<app-localized-text-input [group]="form.controls.name" [label]="…" name="name" />`: the English/Arabic pair of
 * a `LocalizedText` bound to a `localizedTextGroup()`. The Arabic field is `dir="rtl"`; server errors on `nameEn`
 * / `nameAr` land on the right half through `name`.
 */
@Component({
  selector: 'app-localized-text-input',
  imports: [ReactiveFormsModule, FormFieldComponent],
  template: `
    <div class="pair" [formGroup]="group()">
      <app-form-field [label]="labelEn()" [hint]="hint()" [name]="name() + 'En'" [problem]="problem()">
        <input type="text" formControlName="en" dir="ltr" lang="en" autocomplete="off" [attr.maxlength]="maxLength()" />
      </app-form-field>
      <app-form-field [label]="labelAr()" [name]="name() + 'Ar'" [problem]="problem()">
        <input type="text" formControlName="ar" dir="rtl" lang="ar" autocomplete="off" [attr.maxlength]="maxLength()" />
      </app-form-field>
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .pair {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 12px 14px;
    }

    @media (max-width: 719px) {
      .pair {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocalizedTextInputComponent {
  private readonly i18n = inject(I18nService);

  readonly group = input.required<LocalizedTextGroup>();
  readonly label = input.required<string>();
  /** The DTO base name (`name` → `nameEn`/`nameAr`), used to route server errors. */
  readonly name = input('name');
  readonly hint = input<string | null>(null);
  readonly maxLength = input<number | null>(200);
  readonly problem = input<ProblemDetails | null>(null);

  protected readonly labelEn = computed(() => `${this.label()} ${this.i18n.t('shared.localized.enSuffix')}`);
  protected readonly labelAr = computed(() => `${this.label()} ${this.i18n.t('shared.localized.arSuffix')}`);
}

import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';

import type { MergePatientsRequest, PatientDetail, PatientListItem } from '../../../core/api/patients.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { ageParts } from './patient-demographics';

/**
 * Merge a duplicate into this file (CP-11 "merge duplicates (right)", T3): search the duplicate, compare the two files
 * side by side, confirm a gender mismatch explicitly (PAT-100), give the reason (PAT-024). Presentational: the page
 * runs the search (`query`) and the merge (`merge`) and asks the final danger confirmation.
 */
@Component({
  selector: 'app-merge-dialog',
  imports: [ReactiveFormsModule, DialogComponent, SearchBoxComponent, FormFieldComponent, BadgeComponent],
  templateUrl: './merge-dialog.component.html',
  styleUrl: './merge-dialog.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MergeDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  /** The file that survives (keeps its number). */
  readonly survivor = input.required<PatientDetail>();
  readonly candidates = input<readonly PatientListItem[]>([]);
  readonly searching = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly pending = input(false);

  readonly query = output<string>();
  readonly merge = output<MergePatientsRequest>();

  protected readonly selected = signal<PatientListItem | null>(null);
  protected readonly confirmGender = signal(false);
  protected readonly searched = signal(false);
  protected readonly reason = new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(1000)] });

  protected readonly genderMismatch = computed(() => {
    const selected = this.selected();
    return selected !== null && selected.gender !== this.survivor().gender;
  });

  protected readonly problemText = computed(() => {
    const problem = this.problem();
    return problem === null ? null : problemMessage(this.i18n, 'patients', problem);
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.clear());
      }
    });
  }

  protected onSearch(text: string): void {
    this.searched.set(text.trim().length >= 2);
    this.selected.set(null);
    this.query.emit(text.trim());
  }

  protected choose(candidate: PatientListItem): void {
    this.selected.set(candidate);
    this.confirmGender.set(false);
  }

  protected onConfirmGender(event: Event): void {
    this.confirmGender.set((event.target as HTMLInputElement).checked);
  }

  protected submit(): void {
    const selected = this.selected();
    this.reason.markAsTouched();
    if (selected === null || this.reason.invalid || this.pending() || (this.genderMismatch() && !this.confirmGender())) {
      return;
    }

    this.merge.emit({ mergedPatientId: selected.id, reason: this.reason.value.trim(), confirmGenderMismatch: this.genderMismatch() && this.confirmGender() });
  }

  protected canSubmit(): boolean {
    return this.selected() !== null && this.reason.value.trim() !== '' && (!this.genderMismatch() || this.confirmGender()) && !this.pending();
  }

  protected ageText(age: { readonly years: number; readonly months: number; readonly days: number }): string {
    return ageParts(age)
      .map((part) => this.i18n.tf(`patients.age.${part.unit}`, { value: part.value }))
      .join(' ');
  }

  private clear(): void {
    this.selected.set(null);
    this.confirmGender.set(false);
    this.searched.set(false);
    this.reason.reset();
  }
}

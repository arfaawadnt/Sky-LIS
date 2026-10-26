import { ChangeDetectionStrategy, Component, type ElementRef, Injector, afterNextRender, inject, signal, viewChildren } from '@angular/core';
import { type AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, type ValidatorFn, Validators } from '@angular/forms';

import {
  type CreatePreparationRequest,
  PREPARATION_ANSWER_TYPES,
  PREPARATION_GENDERS,
  type PreparationAnswerType,
  type PreparationGender,
  type PreparationListItem,
  type UpdatePreparationRequest,
} from '../../../core/api/catalogue-b.contracts';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { SERVER_ERROR_KEY } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator, requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CatalogueBLookups, type CatalogueBOption, serviceOption } from '../data-b/catalogue-b-lookups';
import { PreparationsFacade } from '../state-b/catalogue-b-list.facade';
import { CatalogueBChipsComponent, type CatalogueBChip } from '../ui-b/catalogue-b-chips.component';
import { CatalogueBPickerComponent } from '../ui-b/catalogue-b-picker.component';
import { CatalogueBEditorPage } from './catalogue-b-editor-page';

export const PREPARATION_MAX_OPTIONS = 30;
export const PREPARATION_OPTION_MAX_LENGTH = 100;

/** One option of an option-list question: a row of the editor (LIS-CR-015 M1). */
type OptionRow = FormControl<string>;

/**
 * A row's own rule while the question is an option list (CAT-211 on the row): at most 100 characters and not the same
 * text as an earlier row, ignoring case. Blank rows are ignored (they are not sent).
 */
export const optionRowRule: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const rows = control.parent;
  if (!(rows instanceof FormArray) || rows.parent?.get('answerType')?.value !== 'OptionList') {
    return null;
  }

  const text = typeof control.value === 'string' ? control.value.trim() : '';
  if (text === '') {
    return null;
  }

  if (text.length > PREPARATION_OPTION_MAX_LENGTH) {
    return { optionTooLong: true };
  }

  const index = rows.controls.findIndex((row) => row === control);
  const key = text.toLowerCase();
  return rows.controls.slice(0, index).some((row) => typeof row.value === 'string' && row.value.trim().toLowerCase() === key) ? { optionDuplicate: true } : null;
};

function optionRow(value: string): OptionRow {
  return new FormControl(value, { nonNullable: true, validators: [optionRowRule] });
}

/**
 * Preparations (SRS CP-46): questions asked at registration for the ordered services — code, bilingual question,
 * answer type (text, yes/no, option list with 2–30 different options, one row each: LIS-CR-015 M1), gender and linked
 * services (none = all).
 */
@Component({
  selector: 'app-catalogue-b-preparations-page',
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    FormFieldComponent,
    LocalizedTextInputComponent,
    CanDirective,
    SaveShortcutDirective,
    UppercaseDirective,
    IconComponent,
    CatalogueBPickerComponent,
    CatalogueBChipsComponent,
  ],
  templateUrl: './preparations.page.html',
  styleUrls: ['./catalogue-b-page.css', './preparations.page.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PreparationsPage extends CatalogueBEditorPage<PreparationListItem, CreatePreparationRequest, UpdatePreparationRequest> {
  protected readonly facade = inject(PreparationsFacade);
  private readonly lookups = inject(CatalogueBLookups);
  private readonly injector = inject(Injector);
  private readonly optionInputs = viewChildren<ElementRef<HTMLInputElement>>('optionInput');

  protected readonly prefix = 'catalogue-b.preparations';
  protected readonly maxOptions = PREPARATION_MAX_OPTIONS;
  protected readonly maxOptionLength = PREPARATION_OPTION_MAX_LENGTH;
  protected readonly answerTypes = PREPARATION_ANSWER_TYPES;
  protected readonly genders = PREPARATION_GENDERS;
  protected readonly knownServices = signal<readonly CatalogueBOption[]>([]);
  protected readonly searchServices = (term: string): Promise<readonly CatalogueBOption[]> => this.lookups.searchServices(term);

  protected readonly columns: readonly TableColumn<PreparationListItem>[] = [
    { key: 'code', header: 'catalogue-b.preparations.column.code', mono: true, sortable: true, width: '120px' },
    { key: 'question', header: 'catalogue-b.preparations.column.question', sortable: true, value: (row) => localizedPick({ en: row.questionEn, ar: row.questionAr }, this.i18n.lang()) },
    { key: 'answerType', header: 'catalogue-b.preparations.column.answerType', value: (row) => this.i18n.t(`catalogue-b.answerType.${row.answerType}`), width: '130px' },
    { key: 'gender', header: 'catalogue-b.preparations.column.gender', value: (row) => this.i18n.t(`catalogue-b.gender.${row.gender}`), width: '110px', secondary: true },
    { key: 'services', header: 'catalogue-b.preparations.column.services', secondary: true },
  ];

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(20)] }),
    question: localizedTextGroup({}, { required: true, maxLength: 400 }),
    answerType: new FormControl<PreparationAnswerType>('Text', { nonNullable: true }),
    options: new FormArray<OptionRow>([], { validators: [(control) => this.optionsRule(control)] }),
    gender: new FormControl<PreparationGender>('Any', { nonNullable: true }),
    serviceIds: new FormControl<readonly string[]>([], { nonNullable: true }),
  });

  protected chips(row: PreparationListItem): readonly CatalogueBChip[] {
    return row.services.map((service) => ({ key: service.id, text: service.code, title: localizedPick({ en: service.nameEn, ar: service.nameAr }, this.i18n.lang()), inactive: !service.isActive }));
  }

  protected isOptionList(): boolean {
    return this.form.controls.answerType.value === 'OptionList';
  }

  protected get optionRows(): FormArray<OptionRow> {
    return this.form.controls.options;
  }

  /** An option list starts with two empty rows: the fewest it may hold (CAT-210). */
  protected onAnswerTypeChange(): void {
    if (this.isOptionList() && this.optionRows.length === 0) {
      this.optionRows.push(optionRow(''));
      this.optionRows.push(optionRow(''));
    }

    this.revalidateOptions();
  }

  protected addOption(): void {
    if (this.readOnly() || this.optionRows.length >= PREPARATION_MAX_OPTIONS) {
      return;
    }

    this.optionRows.push(optionRow(''));
    this.optionRows.markAsDirty();
    this.revalidateOptions();
    this.focusOption(this.optionRows.length - 1);
  }

  protected removeOption(index: number): void {
    if (this.readOnly() || index < 0 || index >= this.optionRows.length) {
      return;
    }

    this.optionRows.removeAt(index);
    this.optionRows.markAsDirty();
    this.revalidateOptions();
  }

  protected moveOption(index: number, offset: -1 | 1): void {
    const target = index + offset;
    if (this.readOnly() || index < 0 || index >= this.optionRows.length || target < 0 || target >= this.optionRows.length) {
      return;
    }

    const row = this.optionRows.at(index);
    this.optionRows.removeAt(index, { emitEvent: false });
    this.optionRows.insert(target, row);
    this.optionRows.markAsDirty();
    this.revalidateOptions();
  }

  /** Enter never submits from a row: it moves to the next row, and the last non-empty row adds one (below 30). */
  protected onOptionEnter(event: Event, index: number): void {
    event.preventDefault();
    if (index < this.optionRows.length - 1) {
      this.focusOption(index + 1);
      return;
    }

    if (this.optionRows.at(index).value.trim() !== '') {
      this.addOption();
    }
  }

  /**
   * Several pasted lines become rows, like a text editor: the first line goes in at the caret, the next lines follow as
   * new rows and the text after the caret ends the last one. A single line keeps the browser's own paste.
   */
  protected onOptionPaste(event: ClipboardEvent, index: number): void {
    const text = event.clipboardData?.getData('text/plain') ?? '';
    if (this.readOnly() || !/[\r\n]/.test(text)) {
      return;
    }

    event.preventDefault();
    const lines = splitOptions(text);
    const row = this.optionRows.at(index);
    const input = event.target instanceof HTMLInputElement ? event.target : null;
    const value = row.value;
    const before = value.slice(0, input?.selectionStart ?? value.length);
    const after = value.slice(input?.selectionEnd ?? value.length);
    const values = lines.length === 0 ? [before + after] : lines.map((line, position) => (position === 0 ? before : '') + line + (position === lines.length - 1 ? after : ''));
    const [first = '', ...rest] = values.map((line) => line.trim());

    row.setValue(first);
    rest.forEach((line, offset) => this.optionRows.insert(index + 1 + offset, optionRow(line)));
    this.optionRows.markAsDirty();
    this.revalidateOptions();
    this.focusOption(index + rest.length);
  }

  /** Re-runs every row's rule (a duplicate depends on the rows above it), then the list's own. */
  protected revalidateOptions(): void {
    this.optionRows.controls.forEach((row) => row.updateValueAndValidity({ onlySelf: true }));
    this.optionRows.updateValueAndValidity();
  }

  /** The message of a row: the server's first, then too long, then duplicate. */
  protected optionError(row: OptionRow): string | null {
    const errors = row.errors;
    if (errors === null || !(row.dirty || row.touched || this.optionRows.dirty)) {
      return null;
    }

    const server: unknown = errors[SERVER_ERROR_KEY];
    if (typeof server === 'string') {
      return server;
    }

    if (errors['optionTooLong'] === true) {
      return this.i18n.tf('catalogue-b.preparations.options.tooLong', { max: PREPARATION_OPTION_MAX_LENGTH });
    }

    return errors['optionDuplicate'] === true ? this.i18n.t('catalogue-b.preparations.options.duplicate') : null;
  }

  /** The message of the whole list (fewer than two, more than thirty, or the server's), once the list was touched. */
  protected optionsError(): string | null {
    const errors = this.optionRows.errors;
    if (errors === null || !(this.optionRows.touched || this.optionRows.dirty)) {
      return null;
    }

    const server: unknown = errors[SERVER_ERROR_KEY];
    if (typeof server === 'string') {
      return server;
    }

    if (errors['optionsTooFew'] === true) {
      return this.i18n.t('catalogue-b.error.CAT-210');
    }

    return errors['optionsTooMany'] === true ? this.i18n.tf('catalogue-b.preparations.options.tooMany', { max: PREPARATION_MAX_OPTIONS }) : null;
  }

  private focusOption(index: number): void {
    afterNextRender({ write: () => this.optionInputs()[index]?.nativeElement.focus() }, { injector: this.injector });
  }

  protected onAnswerTypeFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setExtra('answerType', value === '' ? null : value);
  }

  protected fill(item: PreparationListItem | null): void {
    this.knownServices.set(item?.services.map(serviceOption) ?? []);
    const options = item?.options ?? [];
    this.optionRows.clear({ emitEvent: false });
    options.forEach((option) => this.optionRows.push(optionRow(option), { emitEvent: false }));
    this.form.setValue({
      code: item?.code ?? '',
      question: { en: item?.questionEn ?? '', ar: item?.questionAr ?? '' },
      answerType: item?.answerType ?? 'Text',
      options: [...options],
      gender: item?.gender ?? 'Any',
      serviceIds: item?.services.map((service) => service.id) ?? [],
    });
    this.revalidateOptions();
  }

  protected lockOnEdit(editing: boolean): void {
    if (editing) {
      this.form.controls.code.disable();
    }
  }

  protected createRequest(): CreatePreparationRequest {
    return { code: this.form.controls.code.value.trim(), ...this.updateRequest() };
  }

  protected updateRequest(): UpdatePreparationRequest {
    const value = this.form.getRawValue();
    return {
      questionEn: value.question.en.trim(),
      questionAr: value.question.ar.trim(),
      answerType: value.answerType,
      options: value.answerType === 'OptionList' ? optionValues(value.options) : [],
      gender: value.gender,
      serviceIds: value.serviceIds,
    };
  }

  /**
   * INV-CTB-004 on the client, for the list as a whole: 2–30 different options (the server decides with CAT-210/211).
   * Duplicates and over-long options are the rows' own errors ({@link optionRowRule}).
   */
  private optionsRule(control: AbstractControl): ValidationErrors | null {
    if (control.parent?.get('answerType')?.value !== 'OptionList') {
      return null;
    }

    const options = optionValues(Array.isArray(control.value) ? (control.value as unknown[]) : []);
    const distinct = new Set(options.map((option) => option.toLowerCase()));
    if (options.length < 2 || distinct.size < 2) {
      return { optionsTooFew: true };
    }

    return options.length > PREPARATION_MAX_OPTIONS ? { optionsTooMany: true } : null;
  }
}

/** One option per line; blank lines are ignored (a multi-line paste into a row). */
export function splitOptions(text: string): readonly string[] {
  return text
    .split(/\r?\n|\r/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/** The options sent, in row order: trimmed, blank rows ignored. */
export function optionValues(rows: readonly unknown[]): readonly string[] {
  return rows
    .map((row) => (typeof row === 'string' ? row.trim() : ''))
    .filter((row) => row.length > 0);
}

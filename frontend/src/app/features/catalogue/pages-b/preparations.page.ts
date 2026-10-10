import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { type AbstractControl, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, Validators } from '@angular/forms';

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

/**
 * Preparations (SRS CP-46): questions asked at registration for the ordered services — code, bilingual question,
 * answer type (text, yes/no, option list with ≥ 2 options, one per line), gender and linked services (none = all).
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
  styleUrl: './catalogue-b-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PreparationsPage extends CatalogueBEditorPage<PreparationListItem, CreatePreparationRequest, UpdatePreparationRequest> {
  protected readonly facade = inject(PreparationsFacade);
  private readonly lookups = inject(CatalogueBLookups);

  protected readonly prefix = 'catalogue-b.preparations';
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
    options: new FormControl('', { nonNullable: true, validators: [(control) => this.optionsRule(control)] }),
    gender: new FormControl<PreparationGender>('Any', { nonNullable: true }),
    serviceIds: new FormControl<readonly string[]>([], { nonNullable: true }),
  });

  protected chips(row: PreparationListItem): readonly CatalogueBChip[] {
    return row.services.map((service) => ({ key: service.id, text: service.code, title: localizedPick({ en: service.nameEn, ar: service.nameAr }, this.i18n.lang()), inactive: !service.isActive }));
  }

  protected isOptionList(): boolean {
    return this.form.controls.answerType.value === 'OptionList';
  }

  protected onAnswerTypeChange(): void {
    this.form.controls.options.updateValueAndValidity();
  }

  protected onAnswerTypeFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setExtra('answerType', value === '' ? null : value);
  }

  protected fill(item: PreparationListItem | null): void {
    this.knownServices.set(item?.services.map(serviceOption) ?? []);
    this.form.setValue({
      code: item?.code ?? '',
      question: { en: item?.questionEn ?? '', ar: item?.questionAr ?? '' },
      answerType: item?.answerType ?? 'Text',
      options: item?.options.join('\n') ?? '',
      gender: item?.gender ?? 'Any',
      serviceIds: item?.services.map((service) => service.id) ?? [],
    });
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
      options: value.answerType === 'OptionList' ? splitOptions(value.options) : [],
      gender: value.gender,
      serviceIds: value.serviceIds,
    };
  }

  /** INV-CTB-004 on the client: an option list needs 2–30 different options (the server decides with CAT-210/211). */
  private optionsRule(control: AbstractControl): ValidationErrors | null {
    if (control.parent?.get('answerType')?.value !== 'OptionList') {
      return null;
    }

    const options = splitOptions(typeof control.value === 'string' ? control.value : '');
    const distinct = new Set(options.map((option) => option.toLowerCase()));
    if (options.length < 2 || distinct.size < 2) {
      return { [SERVER_ERROR_KEY]: this.i18n.t('catalogue-b.error.CAT-210') };
    }

    if (distinct.size !== options.length || options.length > PREPARATION_MAX_OPTIONS || options.some((option) => option.length > 100)) {
      return { [SERVER_ERROR_KEY]: this.i18n.t('catalogue-b.error.CAT-211') };
    }

    return null;
  }
}

/** One option per line; blank lines are ignored. */
export function splitOptions(text: string): readonly string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

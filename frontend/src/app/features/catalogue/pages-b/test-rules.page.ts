import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { type AbstractControl, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, Validators } from '@angular/forms';

import type { LinkedServiceView, TestRuleIssue, TestRuleListItem, TestRuleRequest, TestRuleValidationResult } from '../../../core/api/catalogue-b.contracts';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SERVER_ERROR_KEY } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
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
import { TestRulesFacade } from '../state-b/catalogue-b-list.facade';
import { CatalogueBChipsComponent, type CatalogueBChip } from '../ui-b/catalogue-b-chips.component';
import { CatalogueBPickerComponent } from '../ui-b/catalogue-b-picker.component';
import { CatalogueBEditorPage } from './catalogue-b-editor-page';

/**
 * Test rules (SRS CP-52): `{A} + {B} >> {C}` — when every trigger service is ordered, registration adds the added
 * services at their own prices. No service on both sides; "Check rule" asks the server for errors and warnings
 * (duplicate triggers, rules feeding each other, unknown or inactive services) without saving.
 */
@Component({
  selector: 'app-catalogue-b-test-rules-page',
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
    IconComponent,
    CatalogueBPickerComponent,
    CatalogueBChipsComponent,
  ],
  templateUrl: './test-rules.page.html',
  styleUrl: './catalogue-b-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TestRulesPage extends CatalogueBEditorPage<TestRuleListItem, TestRuleRequest, TestRuleRequest> {
  protected readonly facade = inject(TestRulesFacade);
  private readonly lookups = inject(CatalogueBLookups);

  protected readonly prefix = 'catalogue-b.testRules';
  protected readonly groups = signal<readonly CatalogueBOption[]>([]);
  protected readonly knownServices = signal<readonly CatalogueBOption[]>([]);
  protected readonly checkResult = signal<TestRuleValidationResult | null>(null);
  protected readonly searchServices = (term: string): Promise<readonly CatalogueBOption[]> => this.lookups.searchServices(term);
  private readonly serviceLabels = computed(() => new Map(this.knownServices().map((service) => [service.id, service])));

  protected readonly columns: readonly TableColumn<TestRuleListItem>[] = [
    { key: 'name', header: 'catalogue-b.testRules.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'group', header: 'catalogue-b.testRules.column.group', secondary: true, value: (row) => (row.clinicalGroupId === null ? '' : localizedPick({ en: row.clinicalGroupNameEn ?? '', ar: row.clinicalGroupNameAr ?? '' }, this.i18n.lang())) },
    { key: 'rule', header: 'catalogue-b.testRules.column.rule' },
  ];

  protected readonly form = new FormGroup(
    {
      name: localizedTextGroup({}, { required: true, maxLength: 200 }),
      clinicalGroupId: new FormControl<string | null>(null),
      triggerServiceIds: new FormControl<readonly string[]>([], { nonNullable: true, validators: [Validators.required] }),
      addedServiceIds: new FormControl<readonly string[]>([], { nonNullable: true, validators: [Validators.required, (control) => this.sidesRule(control)] }),
    },
  );

  constructor() {
    super();
    void this.loadGroups();
  }

  protected chips(services: readonly LinkedServiceView[]): readonly CatalogueBChip[] {
    return services.map((service) => ({ key: service.id, text: service.code, title: localizedPick({ en: service.nameEn, ar: service.nameAr }, this.i18n.lang()), inactive: !service.isActive }));
  }

  protected groupName(option: CatalogueBOption): string {
    return `${option.code ? option.code + ' · ' : ''}${localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang())}`;
  }

  protected issueService(issue: TestRuleIssue): string {
    if (issue.serviceId === null) {
      return '';
    }

    const service = this.serviceLabels().get(issue.serviceId);
    return service ? `${service.code} · ${localizedPick({ en: service.nameEn, ar: service.nameAr }, this.i18n.lang())}` : issue.serviceId.slice(0, 8);
  }

  protected issueText(issue: TestRuleIssue): string {
    const key = `catalogue-b.error.${issue.code}`;
    return this.i18n.has(key) ? this.i18n.t(key) : issue.message;
  }

  protected onSidesChange(): void {
    this.form.controls.addedServiceIds.updateValueAndValidity();
    this.checkResult.set(null);
  }

  protected onGroupFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setExtra('clinicalGroupId', value === '' ? null : value);
  }

  /** "Check rule": the server compares the candidate with the catalogue and the other active rules; nothing is saved. */
  protected async check(): Promise<void> {
    if (!markAllTouched(this.form)) {
      return;
    }

    const value = this.form.getRawValue();
    const result = await this.facade.validate.execute({ ruleId: this.editing()?.id ?? null, triggerServiceIds: value.triggerServiceIds, addedServiceIds: value.addedServiceIds });
    this.checkResult.set(result);
  }

  override openCreate(): void {
    this.checkResult.set(null);
    super.openCreate();
  }

  override openEdit(item: TestRuleListItem): void {
    this.checkResult.set(null);
    super.openEdit(item);
  }

  protected fill(item: TestRuleListItem | null): void {
    this.knownServices.set([...(item?.triggers ?? []), ...(item?.added ?? [])].map(serviceOption));
    this.form.setValue({
      name: { en: item?.nameEn ?? '', ar: item?.nameAr ?? '' },
      clinicalGroupId: item?.clinicalGroupId ?? null,
      triggerServiceIds: item?.triggers.map((service) => service.id) ?? [],
      addedServiceIds: item?.added.map((service) => service.id) ?? [],
    });
  }

  protected lockOnEdit(): void {
    // a rule has no code: everything stays editable
  }

  protected createRequest(): TestRuleRequest {
    return this.updateRequest();
  }

  protected updateRequest(): TestRuleRequest {
    const value = this.form.getRawValue();
    return {
      nameEn: value.name.en.trim(),
      nameAr: value.name.ar.trim(),
      clinicalGroupId: value.clinicalGroupId === '' ? null : value.clinicalGroupId,
      triggerServiceIds: value.triggerServiceIds,
      addedServiceIds: value.addedServiceIds,
    };
  }

  /** INV-CTB-006 on the client: no service both triggers and is added (the server decides with CAT-242). */
  private sidesRule(control: AbstractControl): ValidationErrors | null {
    const triggers: unknown = control.parent?.get('triggerServiceIds')?.value;
    const added: unknown = control.value;
    if (!Array.isArray(triggers) || !Array.isArray(added)) {
      return null;
    }

    return added.some((id) => triggers.includes(id)) ? { [SERVER_ERROR_KEY]: this.i18n.t('catalogue-b.error.CAT-242') } : null;
  }

  private async loadGroups(): Promise<void> {
    try {
      this.groups.set(await this.lookups.clinicalGroups());
    } catch {
      this.groups.set([]);
    }
  }
}

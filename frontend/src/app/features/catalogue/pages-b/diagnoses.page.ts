import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { CreateDiagnosisRequest, DiagnosisListItem, UpdateDiagnosisRequest } from '../../../core/api/catalogue-b.contracts';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
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
import { DiagnosesFacade } from '../state-b/catalogue-b-list.facade';
import { CatalogueBChipsComponent, type CatalogueBChip } from '../ui-b/catalogue-b-chips.component';
import { CatalogueBPickerComponent } from '../ui-b/catalogue-b-picker.component';
import { CatalogueBEditorPage } from './catalogue-b-editor-page';

/** Diagnoses (SRS CP-49): ICD code, bilingual name and the services registration adds when the diagnosis is chosen. */
@Component({
  selector: 'app-catalogue-b-diagnoses-page',
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
  templateUrl: './diagnoses.page.html',
  styleUrl: './catalogue-b-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DiagnosesPage extends CatalogueBEditorPage<DiagnosisListItem, CreateDiagnosisRequest, UpdateDiagnosisRequest> {
  protected readonly facade = inject(DiagnosesFacade);
  private readonly lookups = inject(CatalogueBLookups);

  protected readonly prefix = 'catalogue-b.diagnoses';
  protected readonly knownServices = signal<readonly CatalogueBOption[]>([]);
  protected readonly searchServices = (term: string): Promise<readonly CatalogueBOption[]> => this.lookups.searchServices(term);

  protected readonly columns: readonly TableColumn<DiagnosisListItem>[] = [
    { key: 'icdCode', header: 'catalogue-b.diagnoses.column.icdCode', mono: true, sortable: true, width: '120px' },
    { key: 'name', header: 'catalogue-b.diagnoses.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'services', header: 'catalogue-b.diagnoses.column.services' },
  ];

  protected readonly form = new FormGroup({
    icdCode: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(20)] }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    serviceIds: new FormControl<readonly string[]>([], { nonNullable: true }),
  });

  protected chips(row: DiagnosisListItem): readonly CatalogueBChip[] {
    return row.services.map((service) => ({ key: service.id, text: service.code, title: localizedPick({ en: service.nameEn, ar: service.nameAr }, this.i18n.lang()), inactive: !service.isActive }));
  }

  protected fill(item: DiagnosisListItem | null): void {
    this.knownServices.set(item?.services.map(serviceOption) ?? []);
    this.form.setValue({
      icdCode: item?.icdCode ?? '',
      name: { en: item?.nameEn ?? '', ar: item?.nameAr ?? '' },
      serviceIds: item?.services.map((service) => service.id) ?? [],
    });
  }

  protected lockOnEdit(editing: boolean): void {
    if (editing) {
      this.form.controls.icdCode.disable();
    }
  }

  protected createRequest(): CreateDiagnosisRequest {
    return { icdCode: this.form.controls.icdCode.value.trim(), ...this.updateRequest() };
  }

  protected updateRequest(): UpdateDiagnosisRequest {
    const value = this.form.getRawValue();
    return { nameEn: value.name.en.trim(), nameAr: value.name.ar.trim(), serviceIds: value.serviceIds };
  }
}

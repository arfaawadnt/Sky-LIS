import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { CreateDoctorRequest, DoctorListItem, UpdateDoctorRequest } from '../../../core/api/catalogue-b.contracts';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator, mobileValidator, requiredText } from '../../../shared/forms/validators';
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
import { CatalogueBLookups, type CatalogueBOption } from '../data-b/catalogue-b-lookups';
import { DoctorsFacade } from '../state-b/catalogue-b-list.facade';
import { CatalogueBChipsComponent, type CatalogueBChip } from '../ui-b/catalogue-b-chips.component';
import { CatalogueBPickerComponent } from '../ui-b/catalogue-b-picker.component';
import { CatalogueBEditorPage } from './catalogue-b-editor-page';

/**
 * Doctors (SRS CP-48): referring doctors with code, bilingual name, specialty, occupation, the branches that may pick
 * them (none = every branch), the share % that feeds the doctor-income report, and a phone.
 */
@Component({
  selector: 'app-catalogue-b-doctors-page',
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
  templateUrl: './doctors.page.html',
  styleUrl: './catalogue-b-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorsPage extends CatalogueBEditorPage<DoctorListItem, CreateDoctorRequest, UpdateDoctorRequest> {
  protected readonly facade = inject(DoctorsFacade);
  private readonly lookups = inject(CatalogueBLookups);

  protected readonly prefix = 'catalogue-b.doctors';
  protected readonly branches = signal<readonly CatalogueBOption[]>([]);
  private readonly branchById = computed(() => new Map(this.branches().map((branch) => [branch.id, branch])));

  protected readonly searchBranches = (term: string): Promise<readonly CatalogueBOption[]> => {
    const wanted = term.toLowerCase();
    return Promise.resolve(this.branches().filter((branch) => branch.inactive !== true && (wanted === '' || `${branch.code} ${branch.nameEn} ${branch.nameAr}`.toLowerCase().includes(wanted))));
  };

  protected readonly columns: readonly TableColumn<DoctorListItem>[] = [
    { key: 'code', header: 'catalogue-b.doctors.column.code', mono: true, sortable: true, width: '110px' },
    { key: 'name', header: 'catalogue-b.doctors.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'specialty', header: 'catalogue-b.doctors.column.specialty', sortable: true },
    { key: 'occupation', header: 'catalogue-b.doctors.column.occupation', secondary: true },
    { key: 'branches', header: 'catalogue-b.doctors.column.branches', secondary: true },
    { key: 'sharePercent', header: 'catalogue-b.doctors.column.sharePercent', align: 'end', width: '90px', value: (row) => `${row.sharePercent} %` },
    { key: 'phone', header: 'catalogue-b.doctors.column.phone', mono: true, secondary: true },
  ];

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(20)] }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    specialty: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    occupation: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    branchIds: new FormControl<readonly string[]>([], { nonNullable: true }),
    sharePercent: new FormControl<number | null>(0, { validators: [Validators.required, Validators.min(0), Validators.max(100)] }),
    phone: new FormControl('', { nonNullable: true, validators: [mobileValidator, Validators.maxLength(20)] }),
  });

  constructor() {
    super();
    void this.loadBranches();
  }

  protected chips(row: DoctorListItem): readonly CatalogueBChip[] {
    return row.branchIds.map((id) => {
      const branch = this.branchById().get(id);
      const name = branch ? localizedPick({ en: branch.nameEn, ar: branch.nameAr }, this.i18n.lang()) : id.slice(0, 8);
      return { key: id, text: branch?.code || name, title: name };
    });
  }

  protected fill(item: DoctorListItem | null): void {
    this.form.setValue({
      code: item?.code ?? '',
      name: { en: item?.nameEn ?? '', ar: item?.nameAr ?? '' },
      specialty: item?.specialty ?? '',
      occupation: item?.occupation ?? '',
      branchIds: item?.branchIds ?? [],
      sharePercent: item?.sharePercent ?? 0,
      phone: item?.phone ?? '',
    });
  }

  protected lockOnEdit(editing: boolean): void {
    if (editing) {
      this.form.controls.code.disable();
    }
  }

  protected createRequest(): CreateDoctorRequest {
    return { code: this.form.controls.code.value.trim(), ...this.updateRequest() };
  }

  protected updateRequest(): UpdateDoctorRequest {
    const value = this.form.getRawValue();
    return {
      nameEn: value.name.en.trim(),
      nameAr: value.name.ar.trim(),
      specialty: emptyToNull(value.specialty),
      occupation: emptyToNull(value.occupation),
      branchIds: value.branchIds,
      sharePercent: value.sharePercent ?? 0,
      phone: emptyToNull(value.phone),
    };
  }

  private async loadBranches(): Promise<void> {
    try {
      this.branches.set(await this.lookups.branches());
    } catch {
      this.branches.set([]);
    }
  }
}

function emptyToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

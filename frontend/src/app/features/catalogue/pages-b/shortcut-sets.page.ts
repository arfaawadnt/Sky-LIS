import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { CreateShortcutSetRequest, ShortcutSetListItem, UpdateShortcutSetRequest } from '../../../core/api/catalogue-b.contracts';
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
import { ShortcutSetsFacade } from '../state-b/catalogue-b-list.facade';
import { CatalogueBChipsComponent, type CatalogueBChip } from '../ui-b/catalogue-b-chips.component';
import { CatalogueBPickerComponent } from '../ui-b/catalogue-b-picker.component';
import { CatalogueBEditorPage } from './catalogue-b-editor-page';

/**
 * Shortcut sets (SRS CP-51): a code reception types to drop several services at once, each at its own price; at
 * least one service, kept in the order chosen.
 */
@Component({
  selector: 'app-catalogue-b-shortcut-sets-page',
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
  templateUrl: './shortcut-sets.page.html',
  styleUrl: './catalogue-b-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShortcutSetsPage extends CatalogueBEditorPage<ShortcutSetListItem, CreateShortcutSetRequest, UpdateShortcutSetRequest> {
  protected readonly facade = inject(ShortcutSetsFacade);
  private readonly lookups = inject(CatalogueBLookups);

  protected readonly prefix = 'catalogue-b.shortcutSets';
  protected readonly knownServices = signal<readonly CatalogueBOption[]>([]);
  protected readonly searchServices = (term: string): Promise<readonly CatalogueBOption[]> => this.lookups.searchServices(term);

  protected readonly columns: readonly TableColumn<ShortcutSetListItem>[] = [
    { key: 'code', header: 'catalogue-b.shortcutSets.column.code', mono: true, sortable: true, width: '120px' },
    { key: 'name', header: 'catalogue-b.shortcutSets.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'services', header: 'catalogue-b.shortcutSets.column.services' },
  ];

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(20)] }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    serviceIds: new FormControl<readonly string[]>([], { nonNullable: true, validators: [Validators.required] }),
  });

  protected chips(row: ShortcutSetListItem): readonly CatalogueBChip[] {
    return row.services.map((service) => ({ key: service.id, text: service.code, title: localizedPick({ en: service.nameEn, ar: service.nameAr }, this.i18n.lang()), inactive: !service.isActive }));
  }

  protected fill(item: ShortcutSetListItem | null): void {
    this.knownServices.set(item?.services.map(serviceOption) ?? []);
    this.form.setValue({
      code: item?.code ?? '',
      name: { en: item?.nameEn ?? '', ar: item?.nameAr ?? '' },
      serviceIds: item?.services.map((service) => service.id) ?? [],
    });
  }

  protected lockOnEdit(editing: boolean): void {
    if (editing) {
      this.form.controls.code.disable();
    }
  }

  protected createRequest(): CreateShortcutSetRequest {
    return { code: this.form.controls.code.value.trim(), ...this.updateRequest() };
  }

  protected updateRequest(): UpdateShortcutSetRequest {
    const value = this.form.getRawValue();
    return { nameEn: value.name.en.trim(), nameAr: value.name.ar.trim(), serviceIds: value.serviceIds };
  }
}

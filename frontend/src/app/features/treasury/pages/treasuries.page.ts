import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { type CreateTreasuryRequest, TREASURY_PERMISSIONS, type TreasuryListItem } from '../../../core/api/treasury.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { TreasuryApi } from '../data/treasury.api';

/**
 * Treasury catalogue (SRS CP-28) on the generic master-data page: code (immutable after creation), bilingual
 * name, branch (picked from the active branches of the laboratory, shown as `CODE · name` in the list) and activation.
 * The reference for every catalogue/setup screen: a config object, no own template.
 */
@Component({
  selector: 'app-treasuries-page',
  imports: [MasterDataPageComponent],
  template: `<app-master-data-page [config]="config" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TreasuriesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(TreasuryApi);

  protected readonly config: MasterDataConfig<TreasuryListItem, CreateTreasuryRequest> = {
    i18nPrefix: 'treasury.catalogue',
    columns: [
      { key: 'code', header: 'treasury.catalogue.column.code', mono: true, sortable: true, width: '140px' },
      { key: 'name', header: 'treasury.catalogue.column.name', sortable: true, value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
      { key: 'branchId', header: 'treasury.catalogue.column.branch', secondary: true, value: (item) => this.branchLabel(item.branchId) },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 32, hint: 'treasury.catalogue.hint.code' },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'branchId', kind: 'select', required: true, options: () => this.branchOptions(), hint: 'treasury.catalogue.hint.branchId', immutable: true },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.listTreasuriesPage({ isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.createTreasury(dto),
      update: (id, dto) => this.api.renameTreasury(id, { nameEn: dto.nameEn, nameAr: dto.nameAr }),
      setActive: (id, active) => (active ? this.api.reactivateTreasury(id) : this.api.deactivateTreasury(id)),
    },
    permissions: { create: TREASURY_PERMISSIONS.manage, update: TREASURY_PERMISSIONS.manage, activate: TREASURY_PERMISSIONS.manage },
    // the list endpoint has no text search yet (ListTreasuriesQuery: isActive, page, pageSize)
    searchMode: 'client',
  };

  /** The branches loaded for the picker, so the list column can show the code and name instead of the identifier. */
  private readonly branches = signal<ReadonlyMap<string, SelectOption>>(new Map());

  constructor() {
    void this.i18n.load('treasury');
    void this.branchOptions();
  }

  private async branchOptions(): Promise<readonly SelectOption[]> {
    const options = (await this.api.activeBranches()).map((branch) => ({ value: branch.id, label: `${branch.code} · ${branch.nameEn}`, labelAr: `${branch.code} · ${branch.nameAr}` }));
    this.branches.set(new Map(options.map((option) => [option.value, option])));
    return options;
  }

  private branchLabel(branchId: string): string {
    const option = this.branches().get(branchId);
    return option === undefined ? branchId : this.i18n.lang() === 'ar' && option.labelAr ? option.labelAr : option.label;
  }
}

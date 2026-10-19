import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ORGANIZATION_PERMISSIONS } from '../../../core/api/organization.contracts';
import { type CreateTreasuryRequest, TREASURY_PERMISSIONS, type TreasuryListItem } from '../../../core/api/treasury.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { sortParams } from '../../../shared/forms/paging';
import { CanDirective } from '../../../shared/ui/can.directive';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, MasterDataFilter, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { allPages, csvFileName, downloadCsv, toCsv } from '../data/csv-export';
import { TreasuryApi } from '../data/treasury.api';
import { TreasuryExportButtonComponent } from '../ui/export-button.component';

/**
 * Treasury catalogue (SRS CP-28) on the generic master-data page: code (immutable after creation), bilingual
 * name, branch (picked from the active branches of the laboratory, shown as `CODE · name` in the list) and activation.
 * The reference for every catalogue/setup screen: a config object, no own template.
 */
@Component({
  selector: 'app-treasuries-page',
  imports: [RouterLink, MasterDataPageComponent, CanDirective, TreasuryExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <!-- C11 (change request 2026-10-19): the page is reached from Treasury access, which it links back to -->
      <a pageActions ngProjectAs="[pageActions]" class="btn ghost" routerLink="/treasury/access">{{ i18n.t('treasury.title') }}</a>
      <app-treasury-export-button pageActions ngProjectAs="[pageActions]" *appCan="permissions.export" [exporter]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TreasuriesPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(TreasuryApi);
  private readonly access = inject(PermissionsService);

  protected readonly permissions = TREASURY_PERMISSIONS;

  /** The filter of the list on screen: the export writes exactly these rows (UAT B-10). */
  private filter: MasterDataFilter = { search: '', active: true };

  protected readonly config: MasterDataConfig<TreasuryListItem, CreateTreasuryRequest> = {
    i18nPrefix: 'treasury.catalogue',
    columns: [
      { key: 'code', header: 'treasury.catalogue.column.code', mono: true, sortable: true, width: '140px' },
      { key: 'name', header: 'treasury.catalogue.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (item) => this.name(item) },
      { key: 'branchId', header: 'treasury.catalogue.column.branch', secondary: true, value: (item) => this.branchLabel(item) },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 32, hint: 'treasury.catalogue.hint.code' },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'branchId', kind: 'select', required: true, options: () => this.branchOptions(), hint: 'treasury.catalogue.hint.branchId', immutable: true },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listTreasuriesPage({ ...sortParams(filter.sort), search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.api.createTreasury(dto),
      update: (id, dto) => this.api.renameTreasury(id, { nameEn: dto.nameEn, nameAr: dto.nameAr }),
      setActive: (id, active) => (active ? this.api.reactivateTreasury(id) : this.api.deactivateTreasury(id)),
    },
    permissions: { create: TREASURY_PERMISSIONS.manage, update: TREASURY_PERMISSIONS.manage, activate: TREASURY_PERMISSIONS.manage },
    // the server searches the code and both names, so an Arabic name is found from the English UI (UAT B-08)
    searchMode: 'server',
    // B-03: the server sorts the whole catalogue before paging (TreasurySortKeys.Treasuries)
    sortMode: 'server',
  };

  constructor() {
    void this.i18n.load('treasury');
  }

  /** CSV of the treasuries of the current status filter and search, in the grid's formats (UAT B-10). */
  protected readonly exportCsv = async (): Promise<void> => {
    const filter = this.filter;
    // the same server search as the grid (code and both names, UAT B-08), every page of it
    const rows = await allPages((page, pageSize) => this.api.listTreasuriesPage({ search: filter.search, isActive: filter.active, page, pageSize }));
    const t = (key: string): string => this.i18n.t(`treasury.${key}`);
    downloadCsv(
      csvFileName('treasuries'),
      toCsv(rows, [
        { header: t('catalogue.column.code'), value: (item) => item.code },
        { header: t('csv.nameEn'), value: (item) => item.nameEn },
        { header: t('csv.nameAr'), value: (item) => item.nameAr },
        { header: t('catalogue.column.branch'), value: (item) => this.branchLabel(item) },
        { header: t('csv.active'), value: (item) => this.i18n.t(item.isActive ? 'shared.yes' : 'shared.no') },
      ]),
    );
  };

  private name(item: TreasuryListItem): string {
    return localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang());
  }

  /**
   * The branch picker of the form: only managers create treasuries, so nobody else reads the branch register (UAT A-04);
   * a manager without `organization.view` picks among the branches of the session instead of meeting a 403 (R-08).
   */
  private async branchOptions(): Promise<readonly SelectOption[]> {
    if (!this.access.can(TREASURY_PERMISSIONS.manage)) {
      return [];
    }

    const branches = this.access.can(ORGANIZATION_PERMISSIONS.view) ? await this.api.activeBranches() : await this.api.selectableBranches();
    return branches.map((branch) => ({ value: branch.id, label: `${branch.code} · ${branch.nameEn}`, labelAr: `${branch.code} · ${branch.nameAr}` }));
  }

  /** `CODE · name` from the list itself (the server reads the branch for every reader, UAT A-04); the id only when the branch is unknown. */
  private branchLabel(item: TreasuryListItem): string {
    if (item.branchCode === null) {
      return item.branchId;
    }

    const name = localizedPick({ en: item.branchNameEn ?? '', ar: item.branchNameAr ?? '' }, this.i18n.lang());
    return name === '' ? item.branchCode : `${item.branchCode} · ${name}`;
  }
}

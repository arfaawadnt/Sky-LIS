import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { type BranchListItem, type CreateBranchRequest, type LookupOption, ORGANIZATION_PERMISSIONS } from '../../../core/api/organization.contracts';
import { ProblemCodes, ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { sortParams } from '../../../shared/forms/paging';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, MasterDataFilter, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { allPages, csvFileName, downloadCsv, toCsv } from '../data/csv-export';
import { OrganizationApi } from '../data/organization.api';
import { OrganizationExportButtonComponent } from '../ui/export-button.component';

/** The currency a new branch starts with: the platform default of the `defaultCurrency` parameter. */
const DEFAULT_CURRENCY = 'EGP';

/** The drawer field each field-specific refusal of the server belongs to (UAT B-02 / B-03). */
const FIELD_OF_CODE: Readonly<Record<string, string>> = {
  'ORG-008': 'governorateId',
  'ORG-009': 'currency',
  'ORG-015': 'phone',
  'ORG-016': 'email',
  'ORG-017': 'governorateId',
};

/**
 * Branches (SRS CP-36) on the generic master-data page: code (immutable), bilingual name, country and governorate
 * (platform lists), phone, e-mail, currency and activation. A branch is never deleted, only deactivated; the server
 * refuses the last active branch (ORG-110).
 */
@Component({
  selector: 'app-branches-page',
  imports: [MasterDataPageComponent, CanDirective, OrganizationExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-organization-export-button pageActions ngProjectAs="[pageActions]" *appCan="permissions.export" [exporter]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BranchesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(OrganizationApi);

  protected readonly permissions = ORGANIZATION_PERMISSIONS;

  /** The filter of the list on screen: the export writes exactly these rows (UAT B-10). */
  private filter: MasterDataFilter = { search: '', active: true };

  protected readonly config: MasterDataConfig<BranchListItem, CreateBranchRequest> = {
    i18nPrefix: 'organization.branches',
    sortMode: 'server',
    columns: [
      { key: 'code', header: 'organization.branches.column.code', mono: true, sortable: true, width: '130px' },
      { key: 'name', header: 'organization.branches.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
      { key: 'phone', header: 'organization.branches.column.phone', secondary: true },
      { key: 'email', header: 'organization.branches.column.email', secondary: true },
      { key: 'currency', header: 'organization.branches.column.currency', mono: true, width: '100px' },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 16, hint: 'organization.branches.hint.code' },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'countryId', kind: 'select', options: () => this.countryOptions() },
      { name: 'governorateId', kind: 'select', options: () => this.governorateOptions(), hint: 'organization.branches.hint.governorate' },
      { name: 'phone', kind: 'text', maxLength: 20 },
      { name: 'email', kind: 'text', maxLength: 254 },
      { name: 'currency', kind: 'code', required: true, maxLength: 3, immutable: false, defaultValue: DEFAULT_CURRENCY, hint: 'organization.branches.hint.currency' },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listBranches({ ...sortParams(filter.sort), search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.onField(this.api.createBranch(dto)),
      update: (id, dto) =>
        this.onField(this.api.updateBranch(id, { nameEn: dto.nameEn, nameAr: dto.nameAr, countryId: dto.countryId, governorateId: dto.governorateId, phone: dto.phone, email: dto.email, currency: dto.currency })),
      setActive: (id, active) => this.api.setBranchActive(id, active),
    },
    toDto: (value) => toBranchRequest(value),
    // B-01: ORG-017 judges the pair; correcting either field clears the error so the next save re-checks it.
    linkedFields: [['countryId', 'governorateId']],
    permissions: { create: ORGANIZATION_PERMISSIONS.create, update: ORGANIZATION_PERMISSIONS.edit, activate: ORGANIZATION_PERMISSIONS.manage },
  };

  constructor() {
    void this.i18n.load('organization');
  }

  /** CSV of the branches of the current filter, in the grid's formats (UAT B-10). */
  protected readonly exportCsv = async (): Promise<void> => {
    const filter = this.filter;
    const [rows, countries, governorates] = await Promise.all([
      allPages((page, pageSize) => this.api.listBranches({ search: filter.search, isActive: filter.active, page, pageSize })),
      this.api.countries(),
      this.api.governorates(),
    ]);
    const names = new Map([...countries, ...governorates].map((option) => [option.id, option]));
    const place = (id: string | null): string => {
      const option = id === null ? undefined : names.get(id);
      return option === undefined ? '' : this.localized(option.nameEn, option.nameAr);
    };
    const t = (key: string): string => this.i18n.t(`organization.branches.${key}`);
    downloadCsv(
      csvFileName('branches'),
      toCsv(rows, [
        { header: t('column.code'), value: (row) => row.code },
        { header: this.i18n.t('organization.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('organization.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('field.countryId'), value: (row) => place(row.countryId) },
        { header: t('field.governorateId'), value: (row) => place(row.governorateId) },
        { header: t('column.phone'), value: (row) => row.phone },
        { header: t('column.email'), value: (row) => row.email },
        { header: t('column.currency'), value: (row) => row.currency },
        { header: this.i18n.t('organization.csv.active'), value: (row) => this.i18n.t(row.isActive ? 'shared.yes' : 'shared.no') },
      ]),
    );
  };

  /**
   * A field-specific refusal (e-mail, phone, governorate of another country, currency) is handed to the drawer as a
   * field error, so the field is marked with the translated reason instead of a general banner (UAT B-02 / B-03).
   */
  private async onField<T>(call: Promise<T>): Promise<T> {
    try {
      return await call;
    } catch (error: unknown) {
      const field = error instanceof ProblemError ? FIELD_OF_CODE[error.code] : undefined;
      if (error instanceof ProblemError && field !== undefined) {
        const message = problemMessage(this.i18n, 'organization.branches', error.problem);
        throw new ProblemError({ ...error.problem, code: ProblemCodes.validationFailed, errors: { [field]: [message] } }, error.status);
      }

      throw error;
    }
  }

  private localized(en: string, ar: string): string {
    return localizedPick({ en, ar }, this.i18n.lang());
  }

  private async countryOptions(): Promise<readonly SelectOption[]> {
    return (await this.api.countries()).map(toOption);
  }

  /** Every governorate, labelled with its country so the choice is unambiguous; the server checks the pair (ORG-008). */
  private async governorateOptions(): Promise<readonly SelectOption[]> {
    const [countries, governorates] = await Promise.all([this.api.countries(), this.api.governorates()]);
    const byId = new Map(countries.map((country) => [country.id, country]));
    return governorates.map((governorate) => {
      const country = governorate.parentId === null ? undefined : byId.get(governorate.parentId);
      return {
        value: governorate.id,
        label: country ? `${governorate.nameEn} — ${country.nameEn}` : governorate.nameEn,
        labelAr: country ? `${governorate.nameAr} — ${country.nameAr}` : governorate.nameAr,
      };
    });
  }
}

function toOption(option: LookupOption): SelectOption {
  return { value: option.id, label: option.nameEn, labelAr: option.nameAr };
}

function textOrNull(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : null;
}

/** The drawer's value → the request: empty optional fields travel as null. */
export function toBranchRequest(value: FormValue): CreateBranchRequest {
  const name = (value['name'] ?? { en: '', ar: '' }) as { en: string; ar: string };
  return {
    code: typeof value['code'] === 'string' ? value['code'] : '',
    nameEn: name.en.trim(),
    nameAr: name.ar.trim(),
    countryId: textOrNull(value['countryId']),
    governorateId: textOrNull(value['governorateId']),
    phone: textOrNull(value['phone']),
    email: textOrNull(value['email']),
    currency: typeof value['currency'] === 'string' ? value['currency'] : DEFAULT_CURRENCY,
  };
}

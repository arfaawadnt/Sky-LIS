import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { type BranchListItem, type CreateBranchRequest, type LookupOption, ORGANIZATION_PERMISSIONS } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { OrganizationApi } from '../data/organization.api';

/** The currency a new branch starts with: the platform default of the `defaultCurrency` parameter. */
const DEFAULT_CURRENCY = 'EGP';

/**
 * Branches (SRS CP-36) on the generic master-data page: code (immutable), bilingual name, country and governorate
 * (platform lists), phone, e-mail, currency and activation. A branch is never deleted, only deactivated; the server
 * refuses the last active branch (ORG-110).
 */
@Component({
  selector: 'app-branches-page',
  imports: [MasterDataPageComponent],
  template: `<app-master-data-page [config]="config" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BranchesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(OrganizationApi);

  protected readonly config: MasterDataConfig<BranchListItem, CreateBranchRequest> = {
    i18nPrefix: 'organization.branches',
    columns: [
      { key: 'code', header: 'organization.branches.column.code', mono: true, sortable: true, width: '130px' },
      { key: 'name', header: 'organization.branches.column.name', sortable: true, value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
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
      list: (filter, page, signal) => this.api.listBranches({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.createBranch(dto),
      update: (id, dto) => this.api.updateBranch(id, { nameEn: dto.nameEn, nameAr: dto.nameAr, countryId: dto.countryId, governorateId: dto.governorateId, phone: dto.phone, email: dto.email, currency: dto.currency }),
      setActive: (id, active) => this.api.setBranchActive(id, active),
    },
    toDto: (value) => toBranchRequest(value),
    permissions: { create: ORGANIZATION_PERMISSIONS.create, update: ORGANIZATION_PERMISSIONS.edit, activate: ORGANIZATION_PERMISSIONS.manage },
  };

  constructor() {
    void this.i18n.load('organization');
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

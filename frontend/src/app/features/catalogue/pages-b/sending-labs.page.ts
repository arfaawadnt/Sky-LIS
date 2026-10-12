import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { CATALOGUE_B_PERMISSIONS, type SendingLabListItem, type SendingLabRequest } from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type LocalizedText, localizedPick, localizedToDto } from '../../../shared/forms/localized-text';
import { sortParams } from '../../../shared/forms/paging';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueBApi } from '../data-b/catalogue-b.api';
import { ListedFilter } from '../data-b/listed-filter';
import { CatalogueBLookups, type CatalogueBTierOption, toSelectOption } from '../data-b/catalogue-b-lookups';
import { CatalogueBExportButtonComponent } from '../ui-b/catalogue-b-export-button.component';

/**
 * Sending labs (SRS CP-50; lab-to-lab): a contracted laboratory with its payer and contract tier (chosen together as
 * "Payer — Tier"), courier, phone and default doctor; lab-to-lab registration fills them from here.
 */
@Component({
  selector: 'app-catalogue-b-sending-labs-page',
  imports: [MasterDataPageComponent, CatalogueBExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-catalogue-b-export-button pageActions [export]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SendingLabsPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);
  private readonly lookups = inject(CatalogueBLookups);

  private readonly tiers = signal<readonly CatalogueBTierOption[]>([]);
  private readonly tierById = computed(() => new Map(this.tiers().map((tier) => [tier.id, tier])));

  private readonly listed = new ListedFilter();

  protected readonly exportCsv = (): Promise<void> => this.api.sendingLabs.exportCsv(this.listed.query());

  protected readonly config: MasterDataConfig<SendingLabListItem, SendingLabRequest> = {
    i18nPrefix: 'catalogue-b.sendingLabs',
    sortMode: 'server',
    columns: [
      { key: 'name', header: 'catalogue-b.sendingLabs.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
      { key: 'contract', header: 'catalogue-b.sendingLabs.column.contract', value: (item) => this.tierLabel(item.contractTierId) },
      { key: 'courier', header: 'catalogue-b.sendingLabs.column.courier', value: (item) => localizedPick({ en: item.courierEn, ar: item.courierAr }, this.i18n.lang()) },
      { key: 'phone', header: 'catalogue-b.sendingLabs.column.phone', mono: true, secondary: true },
      { key: 'doctor', header: 'catalogue-b.sendingLabs.column.doctor', secondary: true, value: (item) => (item.doctorId === null ? '' : localizedPick({ en: item.doctorNameEn ?? '', ar: item.doctorNameAr ?? '' }, this.i18n.lang())) },
    ],
    fields: [
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'contractTierId', kind: 'select', required: true, full: true, hint: 'catalogue-b.sendingLabs.hint.contractTierId', options: () => this.loadTierOptions() },
      { name: 'courier', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'phone', kind: 'text', maxLength: 20 },
      { name: 'doctorId', kind: 'select', options: async () => (await this.lookups.doctors()).map(toSelectOption) },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.sendingLabs.list({ ...this.listed.track(filter), ...sortParams(filter.sort), page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.sendingLabs.create(dto),
      update: (id, dto) => this.api.sendingLabs.update(id, dto),
      setActive: (id, active) => this.api.sendingLabs.setActive(id, active),
    },
    toDto: (value, item) => this.toDto(value, item),
    permissions: { create: CATALOGUE_B_PERMISSIONS.create, update: CATALOGUE_B_PERMISSIONS.edit, activate: CATALOGUE_B_PERMISSIONS.manage },
    drawerSize: 'lg',
  };

  constructor() {
    void this.i18n.load('catalogue-b');
    void this.loadTierOptions();
  }

  private tierLabel(tierId: string): string {
    const tier = this.tierById().get(tierId);
    if (!tier) {
      return '';
    }

    const ar = this.i18n.lang() === 'ar';
    return `${ar ? tier.payerNameAr : tier.payerNameEn} — ${ar ? tier.nameAr : tier.nameEn}`;
  }

  private tierOptions: Promise<readonly SelectOption[]> | null = null;

  /** Loaded once per page: feeds the select of the editor and the payer/tier labels of the table. */
  private loadTierOptions(): Promise<readonly SelectOption[]> {
    this.tierOptions ??= this.fetchTierOptions();
    return this.tierOptions;
  }

  private async fetchTierOptions(): Promise<readonly SelectOption[]> {
    try {
      const tiers = await this.lookups.tiers();
      this.tiers.set(tiers);
      return tiers.map((tier) => ({ value: tier.id, label: `${tier.payerNameEn} — ${tier.nameEn}`, labelAr: `${tier.payerNameAr} — ${tier.nameAr}` }));
    } catch {
      return [];
    }
  }

  /** The tier decides the payer (INV-CTB-008); an unknown tier keeps the record's payer so the server refuses it with its code. */
  private toDto(value: FormValue, item: SendingLabListItem | null): SendingLabRequest {
    const tierId = typeof value['contractTierId'] === 'string' ? value['contractTierId'] : '';
    const phone = typeof value['phone'] === 'string' ? value['phone'].trim() : '';
    const doctorId = typeof value['doctorId'] === 'string' && value['doctorId'] !== '' ? value['doctorId'] : null;
    const name = localizedToDto('name', value['name'] as LocalizedText);
    const courier = localizedToDto('courier', value['courier'] as LocalizedText);
    return {
      nameEn: name['nameEn'] ?? '',
      nameAr: name['nameAr'] ?? '',
      payerId: this.tierById().get(tierId)?.payerId ?? item?.payerId ?? '',
      contractTierId: tierId,
      courierEn: courier['courierEn'] ?? '',
      courierAr: courier['courierAr'] ?? '',
      phone: phone === '' ? null : phone,
      doctorId,
    };
  }
}

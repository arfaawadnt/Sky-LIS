import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import {
  ANALYSER_PROTOCOLS,
  type AnalyserModelListItem,
  type AnalyserModelRequest,
  type AnalyserProtocol,
  PHYSICAL_LINKS,
  type PhysicalLink,
  PLATFORM_API,
  PLATFORM_PERMISSIONS,
} from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig } from '../../../shared/ui/master-data-page/master-data.types';
import { CsvExportService } from '../data/csv-export';
import { PlatformApi } from '../data/platform.api';
import { fileStamp } from '../ui/format';

/** SA-15 Analyser models of the integration library: vendor, model, discipline, protocol, physical link, modes, driver. */
@Component({
  selector: 'app-platform-analyser-models-page',
  imports: [MasterDataPageComponent, IconComponent],
  template: `
    <app-master-data-page [config]="config">
      <button pageActions type="button" class="btn ghost" (click)="exportCsv()"><app-icon name="printer" [size]="15" />{{ i18n.t('platform.exportCsv') }}</button>
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnalyserModelsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PlatformApi);
  private readonly csv = inject(CsvExportService);

  protected readonly config: MasterDataConfig<AnalyserModelListItem, AnalyserModelRequest> = {
    i18nPrefix: 'platform.analyserModels',
    columns: [
      { key: 'vendor', header: 'platform.analyserModels.column.vendor', sortable: true },
      { key: 'model', header: 'platform.analyserModels.column.model', sortable: true },
      { key: 'discipline', header: 'platform.analyserModels.column.discipline' },
      { key: 'protocol', header: 'platform.analyserModels.column.protocol', value: (row) => this.i18n.t(`platform.protocol.${row.protocol}`) },
      { key: 'physicalLink', header: 'platform.analyserModels.column.physicalLink', value: (row) => this.i18n.t(`platform.link.${row.physicalLink}`) },
      { key: 'modes', header: 'platform.analyserModels.column.modes', value: (row) => row.modes.join(', '), secondary: true },
      { key: 'driverVersion', header: 'platform.analyserModels.column.driverVersion', mono: true, secondary: true },
    ],
    fields: [
      { name: 'vendor', kind: 'text', required: true, maxLength: 100 },
      { name: 'model', kind: 'text', required: true, maxLength: 100 },
      { name: 'discipline', kind: 'text', required: true, maxLength: 100, hint: 'platform.analyserModels.hint.discipline' },
      { name: 'protocol', kind: 'select', required: true, defaultValue: 'Astm', options: ANALYSER_PROTOCOLS.map((value) => ({ value, label: this.labelEn(`protocol.${value}`), labelAr: this.labelAr(`protocol.${value}`) })) },
      { name: 'physicalLink', kind: 'select', required: true, defaultValue: 'Serial', options: PHYSICAL_LINKS.map((value) => ({ value, label: this.labelEn(`link.${value}`), labelAr: this.labelAr(`link.${value}`) })) },
      { name: 'modes', kind: 'text', maxLength: 400, hint: 'platform.analyserModels.hint.modes' },
      { name: 'driverVersion', kind: 'text', maxLength: 40 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.listAnalyserModels({ search: filter.search || null, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.saveAnalyserModel(null, dto),
      update: (id, dto) => this.api.saveAnalyserModel(id, dto),
      setActive: (id, active) => this.api.setLibraryActive(PLATFORM_API.analyserModels, id, active),
    },
    permissions: { create: PLATFORM_PERMISSIONS.integrationManage, update: PLATFORM_PERMISSIONS.integrationManage, activate: PLATFORM_PERMISSIONS.integrationManage },
    toForm: (item) => ({ ...item, modes: item.modes.join(', ') }),
    toDto: (value: FormValue) => ({
      vendor: String(value['vendor'] ?? ''),
      model: String(value['model'] ?? ''),
      discipline: String(value['discipline'] ?? ''),
      protocol: (value['protocol'] ?? 'Astm') as AnalyserProtocol,
      physicalLink: (value['physicalLink'] ?? 'Serial') as PhysicalLink,
      modes: String(value['modes'] ?? '')
        .split(/[,،;]/)
        .map((mode) => mode.trim())
        .filter((mode) => mode.length > 0),
      driverVersion: String(value['driverVersion'] ?? '').trim() || null,
    }),
  };

  constructor() {
    void this.i18n.load('platform');
  }

  protected async exportCsv(): Promise<void> {
    const rows = await this.api.all((page) => this.api.listAnalyserModels({ search: null, isActive: null, ...page }));
    this.csv.download(`analyser-models-${fileStamp()}`, [
      { header: this.i18n.t('platform.analyserModels.column.vendor'), value: (row: AnalyserModelListItem) => row.vendor },
      { header: this.i18n.t('platform.analyserModels.column.model'), value: (row) => row.model },
      { header: this.i18n.t('platform.analyserModels.column.discipline'), value: (row) => row.discipline },
      { header: this.i18n.t('platform.analyserModels.column.protocol'), value: (row) => row.protocol },
      { header: this.i18n.t('platform.analyserModels.column.physicalLink'), value: (row) => row.physicalLink },
      { header: this.i18n.t('platform.analyserModels.column.modes'), value: (row) => row.modes.join('; ') },
      { header: this.i18n.t('platform.analyserModels.column.driverVersion'), value: (row) => row.driverVersion },
      { header: this.i18n.t('platform.active'), value: (row) => (row.isActive ? this.i18n.t('platform.yes') : this.i18n.t('platform.no')) },
    ], rows);
  }

  /** Option labels of the select fields: static English and Arabic texts (the page translates by language). */
  private labelEn(key: string): string {
    return LABELS[key]?.[0] ?? key;
  }

  private labelAr(key: string): string {
    return LABELS[key]?.[1] ?? key;
  }
}

const LABELS: Readonly<Record<string, readonly [string, string]>> = {
  'protocol.Astm': ['ASTM', 'ASTM'],
  'protocol.Hl7': ['HL7', 'HL7'],
  'protocol.Proprietary': ['Proprietary', 'خاص بالمصنّع'],
  'link.Serial': ['Serial (RS-232)', 'منفذ تسلسلي (RS-232)'],
  'link.Tcp': ['TCP/IP', 'شبكة TCP/IP'],
  'link.File': ['File exchange', 'تبادل ملفات'],
};

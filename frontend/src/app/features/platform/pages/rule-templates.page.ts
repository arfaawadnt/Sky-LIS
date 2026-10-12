import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { PLATFORM_API, PLATFORM_PERMISSIONS, type RuleTemplateListItem, type RuleTemplateRequest } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { CsvExportService } from '../data/csv-export';
import { PlatformApi } from '../data/platform.api';
import { fileStamp } from '../ui/format';
import { sortParams } from '../../../shared/forms/paging';

/** SA-18 Rule templates: reusable medical-rule patterns (name, clinical group, formula, description) laboratories adapt on CP-64. */
@Component({
  selector: 'app-platform-rule-templates-page',
  imports: [MasterDataPageComponent, IconComponent],
  template: `
    <app-master-data-page [config]="config">
      <button pageActions type="button" class="btn ghost" (click)="exportCsv()"><app-icon name="printer" [size]="15" />{{ i18n.t('platform.exportCsv') }}</button>
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RuleTemplatesPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PlatformApi);
  private readonly csv = inject(CsvExportService);

  /** The filter of the list on screen: the export writes exactly these rows (UAT R2-02, as C-04 elsewhere). */
  private filter: MasterDataFilter = { search: '', active: true };

  protected readonly config: MasterDataConfig<RuleTemplateListItem, RuleTemplateRequest> = {
    i18nPrefix: 'platform.ruleTemplates',
    // B-03: the server sorts the whole library before paging (PlatformAdminSortKeys.RuleTemplates)
    sortMode: 'server',
    columns: [
      { key: 'name', header: 'platform.ruleTemplates.column.name', sortable: true },
      { key: 'clinicalGroupName', header: 'platform.ruleTemplates.column.clinicalGroup', sortable: true },
      { key: 'formula', header: 'platform.ruleTemplates.column.formula', mono: true },
      { key: 'description', header: 'platform.ruleTemplates.column.description', secondary: true },
    ],
    fields: [
      { name: 'name', kind: 'text', required: true, maxLength: 150 },
      { name: 'clinicalGroupName', kind: 'text', maxLength: 100 },
      { name: 'formula', kind: 'textarea', required: true, maxLength: 2000, hint: 'platform.ruleTemplates.hint.formula' },
      { name: 'description', kind: 'textarea', maxLength: 1000 },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listRuleTemplates({ ...sortParams(filter.sort), search: filter.search || null, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.api.saveRuleTemplate(null, dto),
      update: (id, dto) => this.api.saveRuleTemplate(id, dto),
      setActive: (id, active) => this.api.setLibraryActive(PLATFORM_API.ruleTemplates, id, active),
    },
    permissions: { create: PLATFORM_PERMISSIONS.integrationManage, update: PLATFORM_PERMISSIONS.integrationManage, activate: PLATFORM_PERMISSIONS.integrationManage },
    toDto: (value: FormValue) => ({
      name: String(value['name'] ?? ''),
      clinicalGroupName: String(value['clinicalGroupName'] ?? '').trim() || null,
      formula: String(value['formula'] ?? ''),
      description: String(value['description'] ?? '').trim() || null,
    }),
  };

  constructor() {
    void this.i18n.load('platform');
  }

  protected async exportCsv(): Promise<void> {
    const rows = await this.api.all((page) => this.api.listRuleTemplates({ search: this.filter.search || null, isActive: this.filter.active, ...page }));
    this.csv.download(`rule-templates-${fileStamp()}`, [
      { header: this.i18n.t('platform.ruleTemplates.column.name'), value: (row: RuleTemplateListItem) => row.name },
      { header: this.i18n.t('platform.ruleTemplates.column.clinicalGroup'), value: (row) => row.clinicalGroupName },
      { header: this.i18n.t('platform.ruleTemplates.column.formula'), value: (row) => row.formula },
      { header: this.i18n.t('platform.ruleTemplates.column.description'), value: (row) => row.description },
      { header: this.i18n.t('platform.active'), value: (row) => (row.isActive ? this.i18n.t('platform.yes') : this.i18n.t('platform.no')) },
    ], rows);
  }
}

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import type { CommentTemplateListItem, CommentTemplateRequest } from '../../../core/api/catalogue-b.contracts';
import { CATALOGUE_B_PERMISSIONS } from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueBApi } from '../data-b/catalogue-b.api';
import { ListedFilter } from '../data-b/listed-filter';
import { CatalogueBLookups, toSelectOption } from '../data-b/catalogue-b-lookups';
import { CatalogueBExportButtonComponent } from '../ui-b/catalogue-b-export-button.component';

/**
 * Comment library (SRS CP-47): reusable report comments inserted with `%` and the short name; an optional clinical
 * group scopes a comment (none = every group). Bilingual text up to 400 characters per language.
 */
@Component({
  selector: 'app-catalogue-b-comment-templates-page',
  imports: [MasterDataPageComponent, CatalogueBExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-catalogue-b-export-button pageActions [export]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentTemplatesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);
  private readonly lookups = inject(CatalogueBLookups);

  private readonly listed = new ListedFilter();

  protected readonly exportCsv = (): Promise<void> => this.api.commentTemplates.exportCsv(this.listed.query());

  protected readonly config: MasterDataConfig<CommentTemplateListItem, CommentTemplateRequest> = {
    i18nPrefix: 'catalogue-b.comments',
    columns: [
      { key: 'shortName', header: 'catalogue-b.comments.column.shortName', mono: true, sortable: true, width: '150px' },
      { key: 'text', header: 'catalogue-b.comments.column.text', value: (item) => localizedPick({ en: item.textEn, ar: item.textAr }, this.i18n.lang()) },
      {
        key: 'group',
        header: 'catalogue-b.comments.column.group',
        secondary: true,
        value: (item) => (item.clinicalGroupId === null ? this.i18n.t('catalogue-b.common.allGroups') : localizedPick({ en: item.clinicalGroupNameEn ?? '', ar: item.clinicalGroupNameAr ?? '' }, this.i18n.lang())),
      },
    ],
    fields: [
      { name: 'shortName', kind: 'code', required: true, maxLength: 32, hint: 'catalogue-b.comments.hint.shortName' },
      { name: 'clinicalGroupId', kind: 'select', hint: 'catalogue-b.comments.hint.clinicalGroupId', options: async () => (await this.lookups.clinicalGroups()).map(toSelectOption) },
      { name: 'text', kind: 'localizedText', required: true, maxLength: 400 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.commentTemplates.list({ ...this.listed.track(filter), page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.commentTemplates.create(dto),
      update: (id, dto) => this.api.commentTemplates.update(id, dto),
      setActive: (id, active) => this.api.commentTemplates.setActive(id, active),
    },
    permissions: { create: CATALOGUE_B_PERMISSIONS.create, update: CATALOGUE_B_PERMISSIONS.edit, activate: CATALOGUE_B_PERMISSIONS.manage },
    drawerSize: 'lg',
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }
}

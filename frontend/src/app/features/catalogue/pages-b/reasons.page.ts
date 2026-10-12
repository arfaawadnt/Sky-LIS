import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { CATALOGUE_B_PERMISSIONS, REASON_KINDS, type ReasonKind, type ReasonListItem, type ReasonRequest } from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { sortParams } from '../../../shared/forms/paging';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueBApi } from '../data-b/catalogue-b.api';
import { ListedFilter } from '../data-b/listed-filter';
import { CatalogueBExportButtonComponent } from '../ui-b/catalogue-b-export-button.component';

const KIND_TONES: Readonly<Record<ReasonKind, BadgeTone>> = {
  Rejection: 'bad',
  Cancellation: 'warn',
  Discount: 'violet',
  Refund: 'info',
  Transfer: 'neutral',
};

/**
 * Reasons (SRS CP-53): the reason lists other modules require on sample rejection, cancellation, discount, refund and
 * money transfer (BR-15: the reason travels with the record). A reason's kind is fixed once created.
 */
@Component({
  selector: 'app-catalogue-b-reasons-page',
  imports: [MasterDataPageComponent, CellTemplateDirective, BadgeComponent, CatalogueBExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <ng-template appCell="kind" [appCellOf]="config" let-row>
        <app-badge [tone]="tone(row.kind)">{{ i18n.t('catalogue-b.reasonKind.' + row.kind) }}</app-badge>
      </ng-template>
      <app-catalogue-b-export-button pageActions [export]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReasonsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);

  private readonly listed = new ListedFilter();

  protected readonly exportCsv = (): Promise<void> => this.api.reasons.exportCsv(this.listed.query());

  protected readonly config: MasterDataConfig<ReasonListItem, ReasonRequest> = {
    i18nPrefix: 'catalogue-b.reasons',
    sortMode: 'server',
    columns: [
      { key: 'kind', header: 'catalogue-b.reasons.column.kind', sortable: true, width: '160px', value: (item) => this.i18n.t(`catalogue-b.reasonKind.${item.kind}`) },
      { key: 'name', header: 'catalogue-b.reasons.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
    ],
    fields: [
      {
        name: 'kind',
        kind: 'select',
        required: true,
        immutable: true,
        hint: 'catalogue-b.reasons.hint.kind',
        options: () => Promise.resolve(REASON_KINDS.map((kind) => ({ value: kind, label: this.i18n.t(`catalogue-b.reasonKind.${kind}`) }))),
      },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.reasons.list({ ...this.listed.track(filter), ...sortParams(filter.sort), page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.reasons.create(dto),
      update: (id, dto) => this.api.reasons.update(id, dto),
      setActive: (id, active) => this.api.reasons.setActive(id, active),
    },
    permissions: { create: CATALOGUE_B_PERMISSIONS.create, update: CATALOGUE_B_PERMISSIONS.edit, activate: CATALOGUE_B_PERMISSIONS.manage },
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }

  protected tone(kind: ReasonKind): BadgeTone {
    return KIND_TONES[kind];
  }
}

import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';

import type { SampleTypeListItem } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { CatalogueAApi, OPTIONS_PAGE_SIZE } from '../data-a/catalogue-a.api';

/**
 * Microbiology › Culture specimens (CP-56, change request LIS-CR-007 C-11): the sample types flagged "Used for
 * cultures", read-only (code, short name, name, status). The flag is set on the sample type itself (CP-43), so the
 * page links there instead of offering an editor of its own.
 */
@Component({
  selector: 'app-catalogue-b-culture-specimens-page',
  imports: [PageHeaderComponent, SectionCardComponent, DataTableComponent, CellTemplateDirective, BadgeComponent, IconComponent, RouterLink],
  template: `
    <app-page-header [title]="i18n.t('catalogue-b.cultureSpecimens.title')" [subtitle]="i18n.t('catalogue-b.cultureSpecimens.subtitle')">
      <a pageActions class="btn ghost" routerLink="../../sample-types"><app-icon name="flask" [size]="15" />{{ i18n.t('catalogue-b.cultureSpecimens.manage') }}</a>
    </app-page-header>
    <app-section-card [padded]="false">
      @if (specimens.error()) {
        <p class="problem" role="alert">{{ i18n.t('shared.error.generic') }}</p>
      }
      <app-data-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="specimens.isLoading()"
        [interactive]="false"
        [caption]="i18n.t('catalogue-b.cultureSpecimens.title')"
        [emptyTitle]="i18n.t('catalogue-b.cultureSpecimens.emptyTitle')"
        [emptyMessage]="i18n.t('catalogue-b.cultureSpecimens.emptyMessage')"
      >
        <ng-template appCell="status" [appCellOf]="rows()" let-row>
          <app-badge [status]="row.isActive ? 'Active' : 'Inactive'" />
        </ng-template>
      </app-data-table>
    </app-section-card>
  `,
  styles: `
    :host { display: block; }
    .problem { margin: var(--sp-3) var(--sp-4); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CultureSpecimensPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueAApi);

  /** Every culture specimen, active or not (the list is short: one page of the options size). */
  protected readonly specimens = resource({
    loader: async ({ abortSignal }): Promise<readonly SampleTypeListItem[]> =>
      (await this.api.listSampleTypes({ search: '', isActive: null, forCulture: true, page: 1, pageSize: OPTIONS_PAGE_SIZE, sort: 'code', direction: 'asc' }, abortSignal)).items,
  });

  protected readonly rows = computed<readonly SampleTypeListItem[]>(() => (this.specimens.hasValue() ? this.specimens.value() : []));

  protected readonly columns: readonly TableColumn<SampleTypeListItem>[] = [
    { key: 'code', header: 'catalogue-b.cultureSpecimens.column.code', mono: true, sortable: true, width: '120px' },
    { key: 'shortName', header: 'catalogue-b.cultureSpecimens.column.shortName', width: '120px' },
    { key: 'name', header: 'catalogue-b.cultureSpecimens.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'status', header: 'catalogue-b.cultureSpecimens.column.status', width: '120px', sortable: true, value: (row) => this.i18n.t(row.isActive ? 'catalogue-b.cultureSpecimens.active' : 'catalogue-b.cultureSpecimens.inactive') },
  ];

  constructor() {
    void this.i18n.load('catalogue-b');
  }
}

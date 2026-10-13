import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';

import {
  CATALOGUE_B_PERMISSIONS,
  REPORT_TEMPLATE_LANGUAGES,
  REPORT_TEMPLATE_TYPES,
  type ReportTemplateLanguage,
  type ReportTemplateListItem,
  type ReportTemplateRequest,
  type ReportTemplateType,
} from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type LocalizedText, localizedPick } from '../../../shared/forms/localized-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { sortParams } from '../../../shared/forms/paging';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueBApi } from '../data-b/catalogue-b.api';
import { ListedFilter } from '../data-b/listed-filter';
import { CatalogueBExportButtonComponent } from '../ui-b/catalogue-b-export-button.component';
import { ReportTemplateImagesComponent } from '../ui-b/report-template-images.component';

/**
 * The option labels of the drawer's Document type and Language selects, in both languages (UAT R2-03). The options are
 * built once when the page opens, possibly before the Arabic dictionary is loaded, so they carry both texts and the
 * select shows the one of the current language (the same words as `catalogue-b.templateType.*` / `templateLanguage.*`).
 */
export const TEMPLATE_TYPE_LABELS: Readonly<Record<ReportTemplateType, readonly [en: string, ar: string]>> = {
  Results: ['Results', 'النتائج'],
  Culture: ['Culture', 'المزرعة'],
  Text: ['Text report', 'تقرير نصي'],
  Claim: ['Claim', 'المطالبة'],
  Receipt: ['Receipt', 'الإيصال'],
  Label: ['Label', 'الملصق'],
  JobOrder: ['Job order', 'أمر الشغل'],
};

export const TEMPLATE_LANGUAGE_LABELS: Readonly<Record<ReportTemplateLanguage, readonly [en: string, ar: string]>> = {
  En: ['English', 'الإنجليزية'],
  Ar: ['Arabic', 'العربية'],
  Both: ['English and Arabic', 'الإنجليزية والعربية'],
};

/**
 * Report templates (SRS CP-54; SA-12 on the Super Admin portal): print layouts by document type with language, QR and
 * optional bilingual header and footer texts. Logo and signature are files of the Files module (intake A10, validation
 * F-02): the register shows whether one is attached, and the row action "Logo, signature and preview" opens a drawer
 * with a thumbnail of each, upload / replace / remove for `catalogue.edit`, and a sample result report laid out with
 * the template. The edit drawer saves the texts only; the images keep their own calls.
 */
@Component({
  selector: 'app-catalogue-b-report-templates-page',
  imports: [MasterDataPageComponent, CellTemplateDirective, BadgeComponent, CatalogueBExportButtonComponent, DrawerComponent, ReportTemplateImagesComponent],
  template: `
    <app-master-data-page [config]="config">
      <ng-template appCell="assets" [appCellOf]="config" let-row>
        @if (row.logoFileId) {
          <app-badge tone="info">{{ i18n.t('catalogue-b.reportTemplates.logo') }}</app-badge>
        }
        @if (row.signatureFileId) {
          <app-badge tone="violet">{{ i18n.t('catalogue-b.reportTemplates.signature') }}</app-badge>
        }
        @if (row.showQr) {
          <app-badge tone="neutral">QR</app-badge>
        }
      </ng-template>
      <app-catalogue-b-export-button pageActions [export]="exportCsv" />
    </app-master-data-page>
    <app-drawer [(open)]="imagesOpen" [title]="imagesTitle()" size="xl" (closed)="imagesOf.set(null)">
      @if (imagesOpen() && imagesOf(); as template) {
        <app-report-template-images [template]="template" (changed)="onImagesChanged()" />
      }
    </app-drawer>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportTemplatesPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);

  private readonly listed = new ListedFilter();
  private readonly page = viewChild.required(MasterDataPageComponent);

  /** The template whose images and preview the drawer shows. */
  protected readonly imagesOf = signal<ReportTemplateListItem | null>(null);
  protected readonly imagesOpen = signal(false);
  protected readonly imagesTitle = signal('');

  protected readonly exportCsv = (): Promise<void> => this.api.reportTemplates.exportCsv(this.listed.query());

  protected readonly config: MasterDataConfig<ReportTemplateListItem, ReportTemplateRequest> = {
    i18nPrefix: 'catalogue-b.reportTemplates',
    sortMode: 'server',
    columns: [
      { key: 'name', header: 'catalogue-b.reportTemplates.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
      { key: 'type', header: 'catalogue-b.reportTemplates.column.type', sortable: true, width: '150px', value: (item) => this.i18n.t(`catalogue-b.templateType.${item.type}`) },
      { key: 'language', header: 'catalogue-b.reportTemplates.column.language', width: '120px', value: (item) => this.i18n.t(`catalogue-b.templateLanguage.${item.language}`) },
      { key: 'assets', header: 'catalogue-b.reportTemplates.column.assets', secondary: true },
    ],
    fields: [
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      {
        name: 'type',
        kind: 'select',
        required: true,
        options: () => Promise.resolve(REPORT_TEMPLATE_TYPES.map((type) => ({ value: type, label: TEMPLATE_TYPE_LABELS[type][0], labelAr: TEMPLATE_TYPE_LABELS[type][1] }))),
      },
      {
        name: 'language',
        kind: 'select',
        required: true,
        defaultValue: 'Both',
        options: () =>
          Promise.resolve(REPORT_TEMPLATE_LANGUAGES.map((language) => ({ value: language, label: TEMPLATE_LANGUAGE_LABELS[language][0], labelAr: TEMPLATE_LANGUAGE_LABELS[language][1] }))),
      },
      { name: 'showQr', kind: 'boolean' },
      { name: 'headerText', kind: 'localizedText', required: false, maxLength: 400, hint: 'catalogue-b.reportTemplates.hint.texts' },
      { name: 'footerText', kind: 'localizedText', required: false, maxLength: 400 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.reportTemplates.list({ ...this.listed.track(filter), ...sortParams(filter.sort), page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.reportTemplates.create(dto),
      update: (id, dto) => this.api.reportTemplates.update(id, dto),
      setActive: (id, active) => this.api.reportTemplates.setActive(id, active),
    },
    toDto: (value) => toRequest(value),
    rowActions: [{ id: 'images', label: 'catalogue-b.reportTemplates.images.open', icon: 'eye', run: (row) => this.openImages(row) }],
    permissions: { create: CATALOGUE_B_PERMISSIONS.create, update: CATALOGUE_B_PERMISSIONS.edit, activate: CATALOGUE_B_PERMISSIONS.manage },
    drawerSize: 'lg',
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }

  protected openImages(template: ReportTemplateListItem): void {
    this.imagesTitle.set(this.i18n.tf('catalogue-b.reportTemplates.images.title', { name: localizedPick({ en: template.nameEn, ar: template.nameAr }, this.i18n.lang()) }));
    this.imagesOf.set(template);
    this.imagesOpen.set(true);
  }

  /** An image was uploaded or removed: the register's Logo / Signature badges follow. */
  protected onImagesChanged(): void {
    this.page().reload();
  }
}

function optional(value: unknown): string | null {
  const text = typeof value === 'string' ? value.trim() : '';
  return text === '' ? null : text;
}

/** Empty header/footer languages travel as null; logo and signature are not part of the request (their own calls, A10). */
export function toRequest(value: FormValue): ReportTemplateRequest {
  const name = value['name'] as LocalizedText;
  const header = value['headerText'] as LocalizedText | undefined;
  const footer = value['footerText'] as LocalizedText | undefined;
  return {
    nameEn: name.en.trim(),
    nameAr: name.ar.trim(),
    type: (value['type'] as ReportTemplateType | null) ?? 'Results',
    showQr: value['showQr'] === true,
    language: (value['language'] as ReportTemplateLanguage | null) ?? 'Both',
    headerTextEn: optional(header?.en),
    headerTextAr: optional(header?.ar),
    footerTextEn: optional(footer?.en),
    footerTextAr: optional(footer?.ar),
  };
}

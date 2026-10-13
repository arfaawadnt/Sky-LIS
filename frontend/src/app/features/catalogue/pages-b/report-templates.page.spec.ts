import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, type Paged, type ReportTemplateListItem, reportTemplateImageUrl } from '../../../core/api/catalogue-b.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ReportTemplatesPage } from './report-templates.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const template: ReportTemplateListItem = {
  id: 't1',
  nameEn: 'Results A4',
  nameAr: 'النتائج',
  type: 'Results',
  logoFileId: 'file-logo',
  signatureFileId: null,
  showQr: true,
  language: 'En',
  headerTextEn: null,
  headerTextAr: null,
  footerTextEn: null,
  footerTextAr: null,
  isActive: true,
};

function page(items: ReportTemplateListItem[]): Paged<ReportTemplateListItem> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

describe('ReportTemplatesPage (SA-12 / CP-54, validation F-02)', () => {
  it('opens the logo, signature and preview drawer of a template from its row', async () => {
    await TestBed.configureTestingModule({
      imports: [ReportTemplatesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    const backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ReportTemplatesPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.reportTemplates).flush(page([template]));
    await settle();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('app-report-template-images')).toBeNull();

    host.querySelector<HTMLButtonElement>('button[aria-label="catalogue-b.reportTemplates.images.open"]')!.click();
    await settle();
    fixture.detectChanges();
    await settle();

    expect(host.querySelector('app-report-template-images')).not.toBeNull();
    backend.expectOne(reportTemplateImageUrl('t1', 'logo')).flush(new Blob(['png']));
  });
});

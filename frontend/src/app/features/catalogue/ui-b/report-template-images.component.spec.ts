import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, input, provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, type ReportTemplateListItem, reportTemplateImageUrl } from '../../../core/api/catalogue-b.contracts';
import type { LaboratoryReportDocument } from '../../../core/api/results.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { DocumentTexts } from '../../../shared/print/document-texts';
import { type ReportTemplateImageUrls, provideReportDocumentRenderer } from '../../../shared/print/report-template-images';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ReportTemplateImagesComponent } from './report-template-images.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** Stands in for the results report renderer: shows what it was given. */
@Component({
  selector: 'app-renderer-stub',
  template: `<div class="stub-doc">{{ doc().language }}|{{ doc().template?.headerTextEn }}|{{ doc().template?.footerTextAr }}|{{ images()?.logoUrl }}|{{ images()?.signatureUrl }}</div><div class="stub-texts">{{ texts()?.lang }}|{{ texts()?.t('results.report.patient') }}</div>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class RendererStubComponent {
  readonly doc = input.required<LaboratoryReportDocument>();
  readonly images = input<ReportTemplateImageUrls | null>(null);
  readonly texts = input<DocumentTexts | null>(null);
}

const template: ReportTemplateListItem = {
  id: 't1',
  nameEn: 'Results A4',
  nameAr: 'النتائج',
  type: 'Results',
  logoFileId: 'file-logo',
  signatureFileId: null,
  showQr: true,
  language: 'Ar',
  headerTextEn: 'Header',
  headerTextAr: 'رأس',
  footerTextEn: 'Footer',
  footerTextAr: 'تذييل',
  isActive: true,
};

describe('ReportTemplateImagesComponent (SA-12 / CP-54, validation F-02)', () => {
  let fixture: ComponentFixture<ReportTemplateImagesComponent>;
  let backend: HttpTestingController;
  let granted: Set<string>;
  let created: number;
  let revoked: string[];
  const toasts = { success: vi.fn(), error: vi.fn() };
  const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };

  beforeEach(() => {
    created = 0;
    revoked = [];
    toasts.success.mockReset();
    toasts.error.mockReset();
    URL.createObjectURL = (): string => `blob:image-${++created}`;
    URL.revokeObjectURL = (url: string): void => void revoked.push(url);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    URL.createObjectURL = original.create;
    URL.revokeObjectURL = original.revoke;
  });

  async function render(permissions: readonly string[], item: ReportTemplateListItem = template): Promise<HTMLElement> {
    granted = new Set(permissions);
    await TestBed.configureTestingModule({
      imports: [ReportTemplateImagesComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        provideReportDocumentRenderer(() => Promise.resolve(RendererStubComponent)),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ToastService, useValue: toasts },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReportTemplateImagesComponent);
    fixture.componentRef.setInput('template', item);
    fixture.detectChanges();
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  async function stable(): Promise<void> {
    await settle();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows a thumbnail of the attached logo, says the signature is missing, and previews the template with its images', async () => {
    const host = await render(['catalogue.view']);
    backend.expectOne(reportTemplateImageUrl('t1', 'logo')).flush(new Blob(['png'], { type: 'image/png' }));
    backend.expectNone(reportTemplateImageUrl('t1', 'signature'));   // not attached: never asked for
    await stable();

    const images = host.querySelectorAll<HTMLImageElement>('.thumb img');
    expect(images.length).toBe(1);
    expect(images[0]!.getAttribute('src')).toBe('blob:image-1');
    expect(host.textContent).toContain('catalogue-b.reportTemplates.images.none');
    // the preview is drawn by the report renderer, in the template's language, with its texts and images
    expect(host.querySelector('.stub-doc')?.textContent).toBe('ar|Header|تذييل|blob:image-1|');
    // a viewer sees no upload or remove
    expect(host.querySelector('input[type="file"]')).toBeNull();
    expect(host.querySelector('.actions')).toBeNull();
  });

  it('uploads a signature for an editor, reads the template again and tells the page', async () => {
    const host = await render(['catalogue.view', 'catalogue.edit']);
    backend.expectOne(reportTemplateImageUrl('t1', 'logo')).flush(new Blob(['png']));
    await stable();
    const changed: ReportTemplateListItem[] = [];
    fixture.componentInstance.changed.subscribe((item) => changed.push(item));

    const inputs = host.querySelectorAll<HTMLInputElement>('input[type="file"]');
    expect(inputs.length).toBe(2);
    expect(inputs[1]!.accept).toBe('image/png,image/jpeg,image/webp');
    const file = new File(['sig'], 'signature.png', { type: 'image/png' });
    Object.defineProperty(inputs[1]!, 'files', { value: [file] });
    inputs[1]!.dispatchEvent(new Event('change'));
    await settle();

    const upload = backend.expectOne((request) => request.method === 'POST' && request.url === reportTemplateImageUrl('t1', 'signature'));
    expect((upload.request.body as FormData).get('file')).toBeInstanceOf(File);
    upload.flush({ image: 'Signature', fileId: 'file-sig', contentType: 'image/png', sizeBytes: 3 });
    await settle();
    const fresh = { ...template, signatureFileId: 'file-sig' };
    backend.expectOne(`${CATALOGUE_B_API.reportTemplates}/t1`).flush(fresh);
    await settle();
    fixture.detectChanges();   // the fresh template's images are read again
    await settle();
    backend.expectOne(reportTemplateImageUrl('t1', 'logo')).flush(new Blob(['png']));
    backend.expectOne(reportTemplateImageUrl('t1', 'signature')).flush(new Blob(['sig']));
    await stable();

    expect(changed).toEqual([fresh]);
    expect(toasts.success).toHaveBeenCalledWith('catalogue-b.reportTemplates.images.saved');
    expect(host.querySelectorAll('.thumb img').length).toBe(2);
    expect(revoked).toContain('blob:image-1');   // the URLs of the first read are released
    expect(host.querySelector('.stub-doc')?.textContent).toBe('ar|Header|تذييل|blob:image-2|blob:image-3');
  });

  it('removes the logo and shows a refusal of the server in the dictionary words', async () => {
    const host = await render(['catalogue.view', 'catalogue.edit']);
    backend.expectOne(reportTemplateImageUrl('t1', 'logo')).flush(new Blob(['png']));
    await stable();

    const remove = [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('catalogue-b.reportTemplates.images.remove'));
    remove!.click();
    await settle();
    backend
      .expectOne((request) => request.method === 'DELETE' && request.url === reportTemplateImageUrl('t1', 'logo'))
      .flush({ code: 'CAT-394', title: 'Not found', status: 404 }, { status: 404, statusText: 'Not Found', headers: { 'Content-Type': 'application/problem+json' } });
    await stable();

    expect(toasts.error).toHaveBeenCalledTimes(1);
    expect(toasts.success).not.toHaveBeenCalled();
    backend.expectNone(`${CATALOGUE_B_API.reportTemplates}/t1`);
  });

  it('lets a bilingual template be previewed in either language', async () => {
    const host = await render(['catalogue.view'], { ...template, language: 'Both', logoFileId: null });
    await stable();
    expect(host.querySelector('.stub-doc')?.textContent?.startsWith('en|')).toBe(true);

    const arabic = [...host.querySelectorAll<HTMLButtonElement>('.langs button')].find((button) => button.textContent?.trim() === 'العربية');
    arabic!.click();
    await stable();
    expect(host.querySelector('.stub-doc')?.textContent?.startsWith('ar|')).toBe(true);
  });

  it('F-12: the preview speaks the template language, never the screen, and the EN/AR switch reloads its captions', async () => {
    const fetched: string[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => {
      const url = String(input);
      fetched.push(url);
      const patient = url === '/i18n/results.ar.json' ? 'المريض' : url === '/i18n/results.en.json' ? 'Patient' : null;
      return new Response(JSON.stringify(patient === null ? {} : { 'report.patient': patient }), { status: 200 });
    });

    // an Arabic template on an English screen
    const host = await render(['catalogue.view'], { ...template, logoFileId: null });
    TestBed.inject(I18nService).setLanguage('en');
    await stable();
    expect(host.querySelector('.stub-texts')?.textContent).toBe('ar|المريض');
    expect(fetched).toEqual(expect.arrayContaining(['/i18n/results.ar.json', '/i18n/shared.ar.json']));

    // a bilingual template: the switch, not the screen, decides
    fixture.componentRef.setInput('template', { ...template, language: 'Both', logoFileId: null });
    await stable();
    expect(host.querySelector('.stub-texts')?.textContent).toBe('en|Patient');
    [...host.querySelectorAll<HTMLButtonElement>('.langs button')].find((button) => button.textContent?.trim() === 'العربية')!.click();
    await stable();
    await stable();
    expect(host.querySelector('.stub-texts')?.textContent).toBe('ar|المريض');
    expect(host.querySelector('.stub-doc')?.textContent?.startsWith('ar|')).toBe(true);
  });
});

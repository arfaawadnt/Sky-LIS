import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { RejectionsFacade } from '../state/rejections.facade';
import { page, sampleDetail, sampleItem, settle } from '../testing/samples.fixtures';
import { RejectionsPage } from './rejections.page';

describe('RejectionsPage (CP-16)', () => {
  let fixture: ComponentFixture<RejectionsPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  async function open(newBarcodeOnReject: boolean): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [RejectionsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        RejectionsFacade,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RejectionsPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([sampleItem({ id: 'rej-1', sampleNumber: 'SMP-2026-00009', status: 'Rejected', rejectionReasonNameEn: 'Hemolysis' })]));
    backend.expectOne((request) => request.url === SAMPLES_API.rejectionReasons).flush(page([{ id: 'r1', kind: 'Rejection', nameEn: 'Hemolysis', nameAr: 'تحلل', isActive: true }]));
    backend.expectOne(SAMPLES_API.settings).flush({ autoCollect: true, autoReceive: true, newBarcodeOnReject, labelCopies: 1 });
    await render();
  }

  function buttons(text: string): HTMLButtonElement[] {
    return [...host.querySelectorAll<HTMLButtonElement>('button')].filter((button) => button.textContent?.trim() === text);
  }

  it('lists rejected tubes and offers replacement only while the policy requires a new barcode', async () => {
    await open(true);

    expect(host.textContent).toContain('SMP-2026-00009');
    expect(buttons('samples.replace.action').length).toBe(1);
    expect(buttons('samples.unreject.action').length).toBe(0);
    backend.verify();
  });

  it('offers reinstatement when the tenant allows the same barcode', async () => {
    await open(false);

    expect(buttons('samples.unreject.action').length).toBe(1);
    backend.verify();
  });

  it('looks a tube up from the query parameter and rejects it with a catalogue reason', async () => {
    await open(true);
    fixture.componentRef.setInput('sampleNumber', 'smp-2026-00001');
    await render();
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Received'));
    await render();

    const reject = buttons('samples.reject.action')[0];
    expect(reject).toBeDefined();
    reject!.click();
    await render();

    const select = host.querySelector<HTMLSelectElement>('app-reject-dialog select');
    expect(select).not.toBeNull();
    select!.value = 'r1';
    select!.dispatchEvent(new Event('change'));
    await render();
    buttons('samples.reject.confirm')[0]!.click();
    await settle();

    const post = backend.expectOne(SAMPLES_API.reject('smp-1'));
    expect(post.request.body).toEqual({ reasonId: 'r1', note: null });
    post.flush(null, { status: 204, statusText: 'No Content' });
    await render();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Rejected'));
    await render();
    backend.verify();
  });
});

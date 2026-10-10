import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { CollectionFacade } from '../state/collection.facade';
import { page, problemBody, sampleDetail, sampleItem, settle } from '../testing/samples.fixtures';
import { CollectionPage } from './collection.page';

describe('CollectionPage (CP-14)', () => {
  let fixture: ComponentFixture<CollectionPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CollectionPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('branch-1') } },
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        CollectionFacade,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CollectionPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([sampleItem({ status: 'Collected' })]));
    backend.expectOne(SAMPLES_API.settings).flush({ autoCollect: false, autoReceive: false, newBarcodeOnReject: true, labelCopies: 1 });
    await render();
  });

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  function scan(value: string): void {
    const input = host.querySelector<HTMLInputElement>('.scan-input');
    expect(input).not.toBeNull();
    input!.value = value;
    input!.dispatchEvent(new Event('input'));
    host.querySelector<HTMLFormElement>('app-scan-box form')!.dispatchEvent(new Event('submit'));
  }

  it("shows today's tubes and both scan modes", () => {
    expect(host.querySelectorAll('app-data-table tbody tr').length).toBe(1);
    expect(host.textContent).toContain('SMP-2026-00001');
    expect(host.querySelectorAll('.modes .mode').length).toBe(2);
  });

  it('receives a scanned tube in Receive mode and shows a success strip', async () => {
    host.querySelectorAll<HTMLButtonElement>('.modes .mode')[1]!.click();
    await render();

    scan('smp-2026-00001');
    await settle();
    const post = backend.expectOne(SAMPLES_API.scan);
    expect(post.request.body).toEqual({ sampleNumber: 'SMP-2026-00001', mode: 'Receive' });
    post.flush(sampleDetail('Received'));
    await render();

    expect(host.querySelector('.feedback.ok')).not.toBeNull();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    await render();
    backend.verify();
  });

  it('shows the refusal of a second collect scan as an error strip', async () => {
    scan('SMP-2026-00001');
    await settle();
    backend.expectOne(SAMPLES_API.scan).flush(problemBody('SMP-010', 409), { status: 409, statusText: 'Conflict' });
    await render();

    const strip = host.querySelector('.feedback.bad');
    expect(strip).not.toBeNull();
    expect(strip?.textContent).toContain('SMP-2026-00001');
    backend.verify();
  });
});

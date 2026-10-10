import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { INTEGRATION_API, INTEGRATION_LOOKUP_API } from '../../../core/api/integration.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { AnalyserTestsFacade } from '../state/analyser-tests.facade';
import { analyser, mapping, page, settle } from '../testing/integration.fixtures';
import { AnalyserTestsPage, toMappingRequest } from './analyser-tests.page';

describe('AnalyserTestsPage (CP-59)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AnalyserTestsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'integration/analyser-tests', component: AnalyserTestsPage }]),
        AnalyserTestsFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(true) } },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function flush(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  async function openWithAnalyser(): Promise<HTMLElement> {
    await TestBed.inject(Router).navigate(['/integration/analyser-tests'], { queryParams: { analyserId: 'an-1' } });
    const fixture = TestBed.createComponent(AnalyserTestsPage);
    await flush();
    backend.expectOne((request) => request.url === INTEGRATION_API.analysers).flush(page([analyser]));
    backend.expectOne((request) => request.url === INTEGRATION_LOOKUP_API.units).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_LOOKUP_API.sampleTypes).flush(page([]));
    backend.expectOne((request) => request.url === `${INTEGRATION_API.analysers}/an-1/sample-codes`).flush([]);
    backend.expectOne((request) => request.url === `${INTEGRATION_API.analysers}/an-1/mappings`).flush(page([mapping]));
    await flush();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the mappings of the analyser chosen in the query string', async () => {
    const host = await openWithAnalyser();
    const rows = host.querySelectorAll('tbody tr[data-row]');
    expect(rows.length).toBe(1);
    expect(rows[0]?.textContent).toContain('WBC');
  });

  it('switches Auto Send on from the row', async () => {
    const host = await openWithAnalyser();
    const toggle = host.querySelector<HTMLInputElement>('tbody input[type="checkbox"]');
    expect(toggle).not.toBeNull();
    toggle?.click();
    await flush();
    const put = backend.expectOne({ method: 'PUT', url: `${INTEGRATION_API.analysers}/an-1/mappings/mp-1/auto-send` });
    expect(put.request.body).toEqual({ enabled: true });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await flush();
    backend.expectOne((request) => request.url === `${INTEGRATION_API.analysers}/an-1/mappings`).flush(page([{ ...mapping, autoSend: true }]));
  });

  it('keeps conversions and range when an inline edit saves', () => {
    const request = toMappingRequest(mapping, { slope: 2, analyserCode: 'WBC2' });
    expect(request).toEqual(expect.objectContaining({ testId: 't-wbc', slope: 2, analyserCode: 'WBC2', conversions: [{ fromValue: '<10', toValue: 'Low' }], rangeOverride: null }));
  });
});

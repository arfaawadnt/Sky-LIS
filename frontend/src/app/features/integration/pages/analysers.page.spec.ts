import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { INTEGRATION_API, INTEGRATION_LOOKUP_API } from '../../../core/api/integration.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { AnalysersFacade } from '../state/analysers.facade';
import { analyser, page, settle } from '../testing/integration.fixtures';
import { analyserForm, AnalysersPage, toAnalyserRequest } from './analysers.page';

describe('AnalysersPage (CP-58)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AnalysersPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        AnalysersFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
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

  it('lists the analysers with their link state and opens the editor with the loaded detail', async () => {
    const fixture = TestBed.createComponent(AnalysersPage);
    await flush();
    backend.expectOne((request) => request.url === INTEGRATION_API.analysers).flush(page([analyser]));
    backend.expectOne((request) => request.url === INTEGRATION_API.siteAgents).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_LOOKUP_API.clinicalGroups).flush(page([{ id: 'cg-1', code: 'HEM', nameEn: 'Haematology', nameAr: 'أمراض الدم' }]));
    backend.expectOne((request) => request.url === INTEGRATION_LOOKUP_API.sampleTypes).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_API.analyserModels).flush([]);
    await flush();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const row = host.querySelector('tbody tr[data-row]');
    expect(row?.textContent).toContain('XN550');
    expect(row?.textContent).toContain('integration.link.Connected');

    const edit = [...host.querySelectorAll<HTMLButtonElement>('tbody button')].find((button) => button.getAttribute('aria-label') === 'integration.edit');
    edit?.click();
    await flush();
    backend.expectOne(`${INTEGRATION_API.analysers}/an-1`).flush({
      ...analyser,
      defaultSampleTypeId: null,
      analyserModelId: null,
      connection: { kind: 'Tcp', serialSettings: null, serialPort: null, host: '10.0.0.5', port: 5000, tcpRole: 'Client', folderPath: null },
      autoPrint: false,
      downloadOrders: false,
      appendUnregisteredTests: false,
      appendUnregisteredSamples: false,
      overwriteReviewed: true,
      overwriteVerified: false,
      printCopies: 2,
      flags: [{ code: 'CLOT', action: 'Stop', description: null }],
    });
    await flush();
    fixture.detectChanges();
    expect(document.querySelector<HTMLInputElement>('input[formcontrolname="host"]')?.value).toBe('10.0.0.5');
  });

  it('sends only the fields of the chosen link kind', () => {
    const form = analyserForm();
    form.patchValue({ code: 'XN', name: { en: 'X', ar: 'س' }, clinicalGroupId: 'cg', kind: 'Tcp', host: ' 10.0.0.9 ', port: 4000, tcpRole: 'Server', serialSettings: '9600,n,8,1', mode: 'Download' });
    const request = toAnalyserRequest(form);
    expect(request.connection).toEqual({ kind: 'Tcp', serialSettings: null, serialPort: null, host: '10.0.0.9', port: 4000, tcpRole: 'Server', folderPath: null });
    expect(request.downloadOrders).toBe(true);
    expect(request.defaultSampleTypeId).toBeNull();
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { INTEGRATION_API } from '../../../core/api/integration.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PcrKitsFacade } from '../state/pcr-kits.facade';
import { kit, page, settle } from '../testing/integration.fixtures';
import { geneRow, kitForm, PcrKitsPage, toKitRequest } from './pcr-kits.page';

describe('PcrKitsPage (CP-62)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PcrKitsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PcrKitsFacade,
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

  it('lists the kits and simulates a pasted run file', async () => {
    const fixture = TestBed.createComponent(PcrKitsPage);
    await flush();
    backend.expectOne((request) => request.url === INTEGRATION_API.pcrKits).flush(page([kit]));
    backend.expectOne((request) => request.url === INTEGRATION_API.analysers).flush(page([]));
    await flush();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(1);

    const simulate = [...host.querySelectorAll<HTMLButtonElement>('tbody button')].find((button) => button.getAttribute('aria-label') === 'integration.kits.simulate');
    simulate?.click();
    await flush();
    fixture.detectChanges();
    const content = document.querySelector<HTMLTextAreaElement>('[data-testid="run-content"]');
    expect(content).not.toBeNull();
    if (content) {
      content.value = 'Optical Head Serial Number,785BR29753\nWell,Target,Sample,Cq\nB03,ORF1a,S1,35.1';
      content.dispatchEvent(new Event('input'));
    }

    const run = [...document.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('integration.kits.run'));
    run?.click();
    await flush();
    const post = backend.expectOne({ method: 'POST', url: `${INTEGRATION_API.pcrKits}/kit-1/simulate` });
    expect(post.request.body).toEqual(expect.objectContaining({ instrumentSerial: '785BR29753' }));
    post.flush({ readable: true, disposition: 'Backup', refusalCode: null, refusalReason: null, fileSerial: '785BR29753', samples: [{ sampleNumber: 'S1', well: 'B03', genes: [{ gene: 'ORF1a', ct: 35.1, call: 'Positive', isInternalControl: false }], verdict: 'Detected', verdictText: 'DETECTED', problem: null }] });
    await flush();
    fixture.detectChanges();
    expect(document.querySelector('[data-testid="verdicts"]')?.textContent).toContain('DETECTED');
  });

  it('turns the editor into the API shape and refuses non-numeric thresholds', () => {
    const form = kitForm();
    form.patchValue({ name: ' Kit ', analyserId: 'an-2', analyserTestCode: 'COVID', resultFolder: 'in', backupFolder: 'bk', failedFolder: 'fail', noAmplificationTokens: 'N/A, Undetermined, ' });
    form.controls.genes.push(geneRow('N', false, '38', '40'));
    form.controls.genes.push(geneRow('IC', true, '40', '40'));

    const request = toKitRequest(form);
    expect(request?.name).toBe('Kit');
    expect(request?.layout.noAmplificationTokens).toEqual(['N/A', 'Undetermined']);
    expect(request?.genes).toEqual([
      { name: 'N', isInternalControl: false, positiveCtMax: 38, borderlineCtMax: 40 },
      { name: 'IC', isInternalControl: true, positiveCtMax: 40, borderlineCtMax: 40 },
    ]);

    form.controls.genes.at(0).controls.positiveCtMax.setValue('abc');
    expect(toKitRequest(form)).toBeNull();
  });
});

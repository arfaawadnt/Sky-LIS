import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_A_API, type ServiceSequenceRow } from '../../../core/api/catalogue-a.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ServiceSequenceFacade } from '../state-a/service-sequence.facade';
import { ServiceSequencePage } from './service-sequence.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function row(id: string, code: string, sequence: number): ServiceSequenceRow {
  return { id, code, nameEn: code, nameAr: code, type: 'Test', clinicalGroupNameEn: 'Chemistry', clinicalGroupNameAr: 'الكيمياء', sequence, isActive: true };
}

describe('ServiceSequencePage (CP-42)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ServiceSequencePage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        ServiceSequenceFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<ServiceSequencePage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(ServiceSequencePage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.serviceSequence).flush([row('a', 'GLU', 1), row('b', 'CHOL', 2), row('c', 'HDL', 3)]);
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  function inputs(host: HTMLElement): HTMLInputElement[] {
    return Array.from(host.querySelectorAll<HTMLInputElement>('input.seq'));
  }

  function type(input: HTMLInputElement | undefined, value: string): void {
    if (!input) {
      throw new Error('input missing');
    }

    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function saveButton(host: HTMLElement): HTMLButtonElement {
    const button = Array.from(host.querySelectorAll<HTMLButtonElement>('app-page-header button')).find((candidate) => candidate.classList.contains('primary'));
    if (!button) {
      throw new Error('save button missing');
    }

    return button;
  }

  it('flags duplicates and refuses to save until the sequences are unique', async () => {
    const { fixture, host } = await render();
    expect(inputs(host).map((input) => input.value)).toEqual(['1', '2', '3']);

    type(inputs(host)[0], '2');
    await fixture.whenStable();
    expect(host.querySelector('.warn-strip')?.textContent).toContain('catalogue-a.sequence.duplicates');
    expect(saveButton(host).disabled).toBe(true);

    type(inputs(host)[1], '0');
    await fixture.whenStable();
    expect(host.textContent).toContain('catalogue-a.sequence.invalid');
  });

  it('swaps two sequences in one PUT of the changed rows and reloads', async () => {
    const { fixture, host } = await render();

    type(inputs(host)[0], '2');
    type(inputs(host)[1], '1');
    await fixture.whenStable();
    expect(saveButton(host).disabled).toBe(false);

    saveButton(host).click();
    await settle();
    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === CATALOGUE_A_API.serviceSequence);
    expect(put.request.body).toEqual({ items: [{ serviceId: 'a', sequence: 2 }, { serviceId: 'b', sequence: 1 }] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    TestBed.tick();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_A_API.serviceSequence).flush([row('b', 'CHOL', 1), row('a', 'GLU', 2), row('c', 'HDL', 3)]);
    await settle();
    await fixture.whenStable();

    expect(inputs(host).map((input) => input.value)).toEqual(['1', '2', '3']);
    backend.verify();
  });
});

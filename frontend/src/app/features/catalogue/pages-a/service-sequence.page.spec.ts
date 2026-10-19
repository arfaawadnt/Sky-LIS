import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_A_API, CATALOGUE_A_PERMISSIONS, type ServiceSequenceRow } from '../../../core/api/catalogue-a.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { CatalogueAOptions } from '../state-a/catalogue-a-options';
import { ServiceSequenceFacade } from '../state-a/service-sequence.facade';
import { ServiceSequencePage } from './service-sequence.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function row(id: string, code: string, sequence: number, clinicalGroupId: string | null = 'g1'): ServiceSequenceRow {
  return { id, code, nameEn: code, nameAr: code, type: 'Test', clinicalGroupId, clinicalGroupNameEn: 'Chemistry', clinicalGroupNameAr: 'الكيمياء', sequence, isActive: true };
}

/** jsdom has no DragEvent: a plain event with the members the page reads. */
function dragEvent(type: string, clientY = 0): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'clientY', { value: clientY });
  Object.defineProperty(event, 'dataTransfer', { value: { setData: (): void => undefined, effectAllowed: 'none', dropEffect: 'none' } });
  return event;
}

describe('ServiceSequencePage (CP-42 / CP-42b)', () => {
  let backend: HttpTestingController;
  let allowed: (key: string) => boolean = () => true;

  beforeEach(async () => {
    allowed = () => true;
    await TestBed.configureTestingModule({
      imports: [ServiceSequencePage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        ServiceSequenceFacade,
        CatalogueAOptions,
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(rows: readonly ServiceSequenceRow[] = [row('a', 'GLU', 1), row('b', 'CHOL', 2), row('c', 'HDL', 3)]): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<ServiceSequencePage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(ServiceSequencePage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.serviceSequence).flush(rows);
    backend.match((request) => request.url === CATALOGUE_A_API.clinicalGroups).forEach((request) => request.flush({ items: [{ id: 'g1', code: 'CHEM', nameEn: 'Chemistry', nameAr: 'الكيمياء', isActive: true }], page: 1, pageSize: 200, totalCount: 1, totalPages: 1 }));
    // the shared options service also loads the lists the sequence page does not use; pending requests would keep the fixture unstable
    for (const url of [CATALOGUE_A_API.sampleTypes, CATALOGUE_A_API.units, CATALOGUE_A_API.patientConditions]) {
      backend.match((request) => request.url === url).forEach((request) => request.flush({ items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 }));
    }
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  function order(host: HTMLElement): string[] {
    return Array.from(host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]')).map((tr) => tr.getAttribute('data-row') ?? '');
  }

  function inputs(host: HTMLElement): HTMLInputElement[] {
    return Array.from(host.querySelectorAll<HTMLInputElement>('input.seq'));
  }

  function rowElement(host: HTMLElement, id: string): HTMLTableRowElement {
    const tr = Array.from(host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]')).find((candidate) => candidate.getAttribute('data-row') === id);
    if (!tr) {
      throw new Error(`row ${id} missing`);
    }

    return tr;
  }

  function button(tr: HTMLElement, selector: string): HTMLButtonElement {
    const found = tr.querySelector<HTMLButtonElement>(selector);
    if (!found) {
      throw new Error(`${selector} missing`);
    }

    return found;
  }

  function saveButton(host: HTMLElement): HTMLButtonElement {
    const found = Array.from(host.querySelectorAll<HTMLButtonElement>('app-page-header button')).find((candidate) => candidate.classList.contains('primary'));
    if (!found) {
      throw new Error('save button missing');
    }

    return found;
  }

  async function commit(fixture: { whenStable(): Promise<unknown> }, input: HTMLInputElement | undefined, value: string): Promise<void> {
    if (!input) {
      throw new Error('input missing');
    }

    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  it('moves a row with the arrow buttons, renumbers within the occupied numbers and saves only the changed rows', async () => {
    const { fixture, host } = await render([row('a', 'GLU', 10), row('b', 'CHOL', 20), row('c', 'HDL', 45)]);
    expect(order(host)).toEqual(['a', 'b', 'c']);
    expect(button(rowElement(host, 'a'), '.move-up').disabled).toBe(true);
    expect(button(rowElement(host, 'c'), '.move-down').disabled).toBe(true);
    expect(saveButton(host).disabled).toBe(true);

    button(rowElement(host, 'c'), '.move-up').click();
    await fixture.whenStable();
    expect(order(host)).toEqual(['a', 'c', 'b']);
    expect(inputs(host).map((input) => input.value)).toEqual(['10', '20', '45']);
    expect(host.querySelector('.dirty-bar')?.textContent).toContain('catalogue-a.sequence.pending');
    expect(host.querySelector<HTMLFieldSetElement>('fieldset.filters-set')?.disabled).toBe(true);
    expect(host.textContent).toContain('catalogue-a.sequence.moved');
    expect(document.activeElement).toBe(button(rowElement(host, 'c'), '.move-up'));

    saveButton(host).click();
    await settle();
    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === CATALOGUE_A_API.serviceSequence);
    expect(put.request.body).toEqual({ items: [{ serviceId: 'c', sequence: 20 }, { serviceId: 'b', sequence: 45 }] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    TestBed.tick();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_A_API.serviceSequence).flush([row('a', 'GLU', 10), row('c', 'HDL', 20), row('b', 'CHOL', 45)]);
    await settle();
    await fixture.whenStable();

    expect(order(host)).toEqual(['a', 'c', 'b']);
    expect(host.querySelector('.dirty-bar')).toBeNull();
    expect(host.querySelector<HTMLFieldSetElement>('fieldset.filters-set')?.disabled).toBe(false);
    backend.verify();
  });

  it('drags a row onto another with native drag and drop, before or after it', async () => {
    const { fixture, host } = await render();
    const handle = button(rowElement(host, 'a'), '.handle');
    expect(handle.getAttribute('draggable')).toBe('true');
    expect(handle.getAttribute('aria-label')).toContain('catalogue-a.sequence.dragHandle');

    handle.dispatchEvent(dragEvent('dragstart'));
    await fixture.whenStable();
    expect(rowElement(host, 'a').classList).toContain('dragging');

    // over the lower half of HDL (jsdom rects are 0 × 0: a positive clientY is below the middle)
    const over = dragEvent('dragover', 5);
    rowElement(host, 'c').querySelector('td')?.dispatchEvent(over);
    await fixture.whenStable();
    expect(over.defaultPrevented).toBe(true);
    expect(rowElement(host, 'c').classList).toContain('drop-after');

    rowElement(host, 'c').dispatchEvent(dragEvent('drop'));
    await fixture.whenStable();
    expect(order(host)).toEqual(['b', 'c', 'a']);
    expect(inputs(host).map((input) => input.value)).toEqual(['1', '2', '3']);
    expect(host.querySelector('.dragging')).toBeNull();

    // now drag HDL (second) above CHOL (first): the upper half
    button(rowElement(host, 'c'), '.handle').dispatchEvent(dragEvent('dragstart'));
    rowElement(host, 'b').querySelector('td')?.dispatchEvent(dragEvent('dragover', -5));
    await fixture.whenStable();
    expect(rowElement(host, 'b').classList).toContain('drop-before');
    rowElement(host, 'b').dispatchEvent(dragEvent('drop'));
    await fixture.whenStable();
    expect(order(host)).toEqual(['c', 'b', 'a']);
  });

  it('a typed number moves the row to the place of that number; an invalid entry is flagged and then dropped', async () => {
    const { fixture, host } = await render([row('a', 'GLU', 10), row('b', 'CHOL', 20), row('c', 'HDL', 30), row('d', 'LDL', 45)]);

    await commit(fixture, inputs(host)[0], '30');
    expect(order(host)).toEqual(['b', 'c', 'a', 'd']);
    expect(inputs(host).map((input) => input.value)).toEqual(['10', '20', '30', '45']);

    // a hole between 20 and 30: the row lands after CHOL
    await commit(fixture, inputs(host)[3], '25');
    expect(order(host)).toEqual(['b', 'd', 'c', 'a']);

    const first = inputs(host)[0];
    if (!first) {
      throw new Error('input missing');
    }

    first.value = '0';
    first.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(host.textContent).toContain('catalogue-a.sequence.invalid');
    first.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(host.textContent).not.toContain('catalogue-a.sequence.invalid');
    expect(first.value).toBe('10');
    expect(order(host)).toEqual(['b', 'd', 'c', 'a']);
  });

  it('Alt+arrow on a focused row moves it and keeps the focus on the same control', async () => {
    const { fixture, host } = await render();
    const down = button(rowElement(host, 'a'), '.move-down');
    down.focus();
    down.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(order(host)).toEqual(['b', 'a', 'c']);
    expect(document.activeElement).toBe(button(rowElement(host, 'a'), '.move-down'));

    const input = inputs(host)[1];
    input?.focus();
    input?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', altKey: true, bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(order(host)).toEqual(['a', 'b', 'c']);
    expect(host.querySelector('.dirty-bar')).toBeNull();

    // without Alt the row stays
    input?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(order(host)).toEqual(['a', 'b', 'c']);
  });

  it('filters by clinical group through the query and asks before discarding or leaving an unsaved order', async () => {
    const { fixture, host } = await render();
    const page = fixture.componentInstance;
    expect(page.canLeave()).toBe(true);

    const group = host.querySelector<HTMLSelectElement>('select.group-filter');
    if (!group) {
      throw new Error('group filter missing');
    }

    expect(Array.from(group.options).map((option) => option.value)).toEqual(['', 'g1']);
    group.value = 'g1';
    group.dispatchEvent(new Event('change'));
    await settle();
    TestBed.tick();
    const filtered = backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_A_API.serviceSequence && request.params.get('clinicalGroupId') === 'g1');
    filtered.flush([row('b', 'CHOL', 2), row('c', 'HDL', 3)]);
    await settle();
    await fixture.whenStable();
    expect(order(host)).toEqual(['b', 'c']);

    button(rowElement(host, 'c'), '.move-up').click();
    await fixture.whenStable();
    expect(order(host)).toEqual(['c', 'b']);

    const confirm = TestBed.inject(ConfirmService);
    const leaving = page.canLeave();
    expect(leaving).not.toBe(true);
    expect(confirm.current()?.options.title).toBe('catalogue-a.sequence.leaveTitle');
    confirm.resolve(false);
    expect(await leaving).toBe(false);

    const discard = Array.from(host.querySelectorAll<HTMLButtonElement>('.dirty-bar button')).find((candidate) => candidate.classList.contains('ghost'));
    discard?.click();
    await settle();
    expect(confirm.current()?.options.title).toBe('catalogue-a.sequence.discard');
    confirm.resolve(true);
    await settle();
    await fixture.whenStable();
    expect(order(host)).toEqual(['b', 'c']);
    expect(page.canLeave()).toBe(true);
    backend.verify();
  });

  it('is read-only for a viewer without the edit right (UAT B-12)', async () => {
    allowed = (key) => key !== CATALOGUE_A_PERMISSIONS.edit;
    const { fixture, host } = await render();

    expect(inputs(host).every((input) => input.disabled)).toBe(true);
    expect(host.querySelector('.handle')).toBeNull();
    expect(host.querySelector('.move-up')).toBeNull();
    const first = inputs(host)[0];
    if (!first) {
      throw new Error('input missing');
    }

    first.value = '3';
    first.dispatchEvent(new Event('input'));
    first.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(order(host)).toEqual(['a', 'b', 'c']);
    expect(host.textContent).not.toContain('catalogue-a.sequence.discard');
    expect(host.querySelector('.changed')).toBeNull();
  });
});

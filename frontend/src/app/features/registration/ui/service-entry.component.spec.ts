import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { CATALOGUE_A_API } from '../../../core/api/catalogue-a.contracts';
import { REGISTRATION_API, type ServiceEntryItem, type ServiceEntryResult } from '../../../core/api/registration.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { page, settle } from '../testing/registration.fixtures';
import { type PickedService, ServiceEntryComponent } from './service-entry.component';

interface ServiceEntryInternals {
  readonly pickerOpen: () => boolean;
  readonly candidates: () => readonly ServiceEntryItem[];
}

const cbc: ServiceEntryItem = { id: 's-cbc', code: 'CBC', shortName: 'CBC', shortcutNo: 10, nameEn: 'Complete blood count', nameAr: 'صورة دم كاملة', type: 'Test' };
const glu: ServiceEntryItem = { ...cbc, id: 's-glu', code: 'GLU', shortName: 'GLU', shortcutNo: 11, nameEn: 'Glucose', nameAr: 'جلوكوز' };

describe('ServiceEntryComponent (CP-06 service entry, LDM §4.4)', () => {
  let fixture: ComponentFixture<ServiceEntryComponent>;
  let backend: HttpTestingController;
  let added: (readonly PickedService[])[];

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ServiceEntryComponent);
    added = [];
    fixture.componentInstance.added.subscribe((services) => added.push(services));
    await fixture.whenStable();
  });

  function internals(): ServiceEntryInternals {
    return fixture.componentInstance as unknown as ServiceEntryInternals;
  }

  function input(): HTMLInputElement {
    return fixture.nativeElement.querySelector('#reg-service-entry') as HTMLInputElement;
  }

  async function typeAndEnter(text: string): Promise<void> {
    const field = input();
    field.value = text;
    field.dispatchEvent(new Event('input'));
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
  }

  it('resolves the typed text on Enter and emits the service', async () => {
    await typeAndEnter(' 10 ');
    const call = backend.expectOne((request) => request.url === REGISTRATION_API.serviceEntry);
    expect(call.request.params.get('text')).toBe('10');
    call.flush({ kind: 'Service', services: [cbc], setCode: null, setNameEn: null, setNameAr: null, candidates: [] } satisfies ServiceEntryResult);
    await settle();

    expect(added).toEqual([[{ serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم كاملة' }]]);
    fixture.detectChanges();
    expect(input().value).toBe('');
    expect(internals().pickerOpen()).toBe(false);
  });

  it('adds every service of a shortcut set and tells the clerk', async () => {
    await typeAndEnter('LIPID');
    backend.expectOne((request) => request.url === REGISTRATION_API.serviceEntry).flush({ kind: 'ShortcutSet', services: [cbc, glu], setCode: 'LIPID', setNameEn: 'Lipid set', setNameAr: null, candidates: [] } satisfies ServiceEntryResult);
    await settle();

    expect(added[0]?.map((service) => service.code)).toEqual(['CBC', 'GLU']);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
  });

  it('opens the picker with the candidates when the text is unknown or ambiguous', async () => {
    await typeAndEnter('gl');
    backend.expectOne((request) => request.url === REGISTRATION_API.serviceEntry).flush({ kind: 'None', services: [], setCode: null, setNameEn: null, setNameAr: null, candidates: [glu] } satisfies ServiceEntryResult);
    await settle();
    fixture.detectChanges();

    expect(internals().pickerOpen()).toBe(true);
    expect(internals().candidates()).toEqual([glu]);
    const pick = fixture.nativeElement.querySelector('.picker-list .pick') as HTMLButtonElement | null;
    expect(pick?.textContent).toContain('GLU');
    pick?.click();
    await settle();
    expect(added).toEqual([[{ serviceId: 's-glu', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز' }]]);
    expect(internals().pickerOpen()).toBe(false);
    backend.verify();
  });

  it('opens the catalogue picker on F2 and searches it as the clerk types', async () => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F2' }));
    await settle();
    fixture.detectChanges();
    expect(internals().pickerOpen()).toBe(true);

    // UAT A-13: an empty box browses the first page of active services at once
    TestBed.tick();
    await settle();
    const browse = backend.expectOne((request) => request.url === CATALOGUE_A_API.lookupServices);
    expect(browse.request.params.has('search')).toBe(false);
    expect(browse.request.params.get('orderable')).toBe('true');
    browse.flush(page([{ id: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم', type: 'Test', shortcutNo: 1 }]));
    await settle();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.picker-list .pick')?.textContent).toContain('CBC');
    expect(fixture.nativeElement.querySelector('.picker-list')?.textContent).not.toContain('registration.services.noMatch');

    const search = fixture.nativeElement.querySelector('app-search-box input') as HTMLInputElement;
    search.value = 'gluc';
    search.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 350));
    TestBed.tick();
    await settle();

    const lookup = backend.expectOne((request) => request.url === CATALOGUE_A_API.lookupServices);
    expect(lookup.request.params.get('search')).toBe('gluc');
    expect(lookup.request.params.get('orderable')).toBe('true');
    lookup.flush(page([{ id: 's-glu', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز', type: 'Test', shortcutNo: 11 }]));
    await settle();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.picker-list .pick')?.textContent).toContain('GLU');
  });

  it('suggests matching services while the clerk types; ↓ + Enter adds the chosen one without the server resolve', async () => {
    const field = input();
    field.value = 'glu';
    field.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));
    TestBed.tick();
    await settle();

    const lookup = backend.expectOne((request) => request.url === CATALOGUE_A_API.lookupServices);
    expect(lookup.request.params.get('search')).toBe('glu');
    lookup.flush(page([
      { id: 's-glu', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز', type: 'Test', shortcutNo: 11 },
      { id: 's-gtt', code: 'GTT', nameEn: 'Glucose tolerance', nameAr: 'منحنى السكر', type: 'Test', shortcutNo: 12 },
    ]));
    await settle();
    fixture.detectChanges();

    const options = fixture.nativeElement.querySelectorAll('#reg-service-suggestions [role="option"]') as NodeListOf<HTMLElement>;
    expect(options.length).toBe(2);
    expect(field.getAttribute('aria-expanded')).toBe('true');

    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    fixture.detectChanges();
    expect(field.getAttribute('aria-activedescendant')).toBe('reg-service-option-1');

    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    fixture.detectChanges();

    expect(added).toEqual([[{ serviceId: 's-gtt', code: 'GTT', nameEn: 'Glucose tolerance', nameAr: 'منحنى السكر' }]]);
    expect(fixture.nativeElement.querySelector('#reg-service-suggestions')).toBeNull();
    backend.expectNone((request) => request.url === REGISTRATION_API.serviceEntry);
  });

  it('closes the suggestions on Escape and resolves the text on Enter when nothing is chosen', async () => {
    const field = input();
    field.value = 'cbc';
    field.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.lookupServices).flush(page([{ id: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم', type: 'Test', shortcutNo: 10 }]));
    await settle();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#reg-service-suggestions')).not.toBeNull();

    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#reg-service-suggestions')).toBeNull();

    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    const call = backend.expectOne((request) => request.url === REGISTRATION_API.serviceEntry);
    expect(call.request.params.get('text')).toBe('cbc');
    call.flush({ kind: 'Service', services: [cbc], setCode: null, setNameEn: null, setNameAr: null, candidates: [] } satisfies ServiceEntryResult);
    await settle();
    expect(added.length).toBe(1);
  });

  it('falls back to the picker when the lookup fails', async () => {
    await typeAndEnter('xyz');
    backend.expectOne((request) => request.url === REGISTRATION_API.serviceEntry).flush({ type: 'about:blank', title: 'Down', status: 500, code: 'INTERNAL-500' }, { status: 500, statusText: 'Error' });
    await settle();
    expect(internals().pickerOpen()).toBe(true);
    expect(added).toEqual([]);
  });
});

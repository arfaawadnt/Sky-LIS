import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type GovernorateListItem, type LocationListItem, PLATFORM_API, PLATFORM_LOCATIONS_API } from '../../../core/api/platform.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { LocationsDrawerComponent } from './locations-drawer.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/**
 * Lets a flushed answer reach its promise, runs the effects (resource loaders and reloads start) and lets the request reach
 * the backend; `whenStable` would wait for an unflushed resource, so it is used only once nothing is pending.
 */
async function tick(): Promise<void> {
  await settle();
  TestBed.tick();
  await settle();
}

const cairo: GovernorateListItem = { id: 'g-cairo', countryId: 'c-eg', countryNameEn: 'Egypt', countryNameAr: 'مصر', nameEn: 'Cairo', nameAr: 'القاهرة', district: null, remarks: null, isActive: true };
const nasr: LocationListItem = { id: 'c-nasr', parentId: 'g-cairo', nameEn: 'Nasr City', nameAr: 'مدينة نصر', isActive: true };
const seventh: LocationListItem = { id: 'd-7', parentId: 'c-nasr', nameEn: '7th district', nameAr: 'الحي السابع', isActive: false };

@Component({
  imports: [LocationsDrawerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-platform-locations-drawer [governorate]="governorate()" (closed)="closedCount = closedCount + 1" />`,
})
class HostComponent {
  readonly governorate = signal<GovernorateListItem | null>(null);
  closedCount = 0;
}

describe('LocationsDrawerComponent (SA-11b Locations)', () => {
  let backend: HttpTestingController;
  let granted: boolean;
  const toasts: string[] = [];

  beforeEach(async () => {
    granted = true;
    toasts.length = 0;
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => granted } satisfies Pick<PermissionsService, 'can'> },
        { provide: ToastService, useValue: { success: (text: string) => toasts.push(text), error: (text: string) => toasts.push(text), info: (text: string) => toasts.push(text) } satisfies Pick<ToastService, 'success' | 'error' | 'info'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  function column(host: HTMLElement, level: 'cities' | 'districts' | 'areas'): HTMLElement {
    return host.querySelector<HTMLElement>(`app-platform-location-column[level="${level}"]`)!;
  }

  async function open(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<HostComponent>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(HostComponent);
    const host = fixture.nativeElement as HTMLElement;
    fixture.componentInstance.governorate.set(cairo);
    await tick();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([nasr]);
    await fixture.whenStable();
    return { fixture, host };
  }

  it('shows the three linked columns and follows the selection from cities to districts to areas', async () => {
    const { fixture, host } = await open();
    expect(host.querySelector('[role="dialog"] h2')?.textContent).toContain('platform.locations.title');
    expect(column(host, 'cities').textContent).toContain('Nasr City');
    expect(column(host, 'districts').textContent).toContain('platform.locations.pick.districts');
    expect(column(host, 'areas').textContent).toContain('platform.locations.pick.areas');

    column(host, 'cities').querySelector<HTMLButtonElement>('.row-name')!.click();
    await tick();
    backend.expectOne((r) => r.url === `${PLATFORM_LOCATIONS_API.districts.parents}/c-nasr/districts`).flush([seventh]);
    await fixture.whenStable();
    expect(column(host, 'cities').querySelector('.row.selected')?.textContent).toContain('Nasr City');
    expect(column(host, 'districts').textContent).toContain('7th district');
    expect(column(host, 'districts').querySelector('.row.inactive app-badge')?.textContent).toContain('platform.locations.inactive');

    column(host, 'districts').querySelector<HTMLButtonElement>('.row-name')!.click();
    await tick();
    backend.expectOne((r) => r.url === `${PLATFORM_LOCATIONS_API.areas.parents}/d-7/areas`).flush([]);
    await fixture.whenStable();
    expect(column(host, 'areas').textContent).toContain('platform.locations.empty');
    expect(column(host, 'areas').querySelector('.row-name')).toBeNull();
  });

  it('adds a city through the inline editor, closes it on success and shows a duplicate refusal in the column (GEO-102)', async () => {
    const { fixture, host } = await open();
    const cities = column(host, 'cities');
    cities.querySelector<HTMLButtonElement>('.col-head button')!.click();
    await fixture.whenStable();
    const en = cities.querySelector<HTMLInputElement>('input[formcontrolname="en"]')!;
    const ar = cities.querySelector<HTMLInputElement>('input[formcontrolname="ar"]')!;
    en.value = 'Maadi';
    en.dispatchEvent(new Event('input'));
    ar.value = 'المعادي';
    ar.dispatchEvent(new Event('input'));
    cities.querySelector<HTMLFormElement>('form.editor')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await tick();

    const post = backend.expectOne({ method: 'POST', url: `${PLATFORM_API.governorates}/g-cairo/cities` });
    expect(post.request.body).toEqual({ nameEn: 'Maadi', nameAr: 'المعادي' });
    post.flush(
      { type: 'about:blank', title: 'Exists', status: 409, code: 'GEO-102', traceId: null, correlationId: null },
      { status: 409, statusText: 'Conflict' },
    );
    await tick();
    await fixture.whenStable();
    expect(cities.querySelector('form.editor')).not.toBeNull();   // the editor stays so the name can be corrected
    // the dictionary is not loaded in specs: the generic text stands for `platform.locations.error.GEO-102`
    expect(cities.querySelector('.problem')?.textContent).toContain('shared.error.generic');
    expect(column(host, 'districts').querySelector('.problem')).toBeNull();

    en.value = 'New Maadi';
    en.dispatchEvent(new Event('input'));
    cities.querySelector<HTMLFormElement>('form.editor')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await tick();
    backend.expectOne({ method: 'POST', url: `${PLATFORM_API.governorates}/g-cairo/cities` }).flush({ id: 'c-new' }, { status: 201, statusText: 'Created' });
    await tick();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([nasr, { ...nasr, id: 'c-new', nameEn: 'New Maadi' }]);
    await fixture.whenStable();
    expect(cities.querySelector('form.editor')).toBeNull();
    expect(toasts).toEqual(['platform.locations.saved']);
    expect(cities.textContent).toContain('New Maadi');
  });

  it('renames and deactivates a row from its buttons', async () => {
    const { fixture, host } = await open();
    const cities = column(host, 'cities');
    cities.querySelector<HTMLButtonElement>('.row-actions button')!.click();   // rename
    await fixture.whenStable();
    expect(cities.querySelector<HTMLInputElement>('input[formcontrolname="en"]')?.value).toBe('Nasr City');
    const en = cities.querySelector<HTMLInputElement>('input[formcontrolname="en"]')!;
    en.value = 'Nasr';
    en.dispatchEvent(new Event('input'));
    cities.querySelector<HTMLFormElement>('form.editor')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await tick();
    const put = backend.expectOne({ method: 'PUT', url: `${PLATFORM_LOCATIONS_API.cities.items}/c-nasr` });
    expect(put.request.body).toEqual({ nameEn: 'Nasr', nameAr: 'مدينة نصر' });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await tick();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([{ ...nasr, nameEn: 'Nasr' }]);
    await fixture.whenStable();

    cities.querySelectorAll<HTMLButtonElement>('.row-actions button')[1]!.click();   // deactivate
    await tick();
    backend.expectOne({ method: 'POST', url: `${PLATFORM_LOCATIONS_API.cities.items}/c-nasr/deactivate` }).flush(null, { status: 204, statusText: 'No Content' });
    await tick();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([]);
    await fixture.whenStable();
    expect(toasts).toEqual(['platform.locations.saved', 'platform.locations.deactivated']);
  });

  it('hides add, rename and activate without tenancy.manage and reports the close', async () => {
    granted = false;
    const { fixture, host } = await open();
    const cities = column(host, 'cities');
    expect(cities.querySelector('.col-head button')).toBeNull();
    expect(cities.querySelector('.row-actions')).toBeNull();
    expect(cities.querySelector('.row-name')).not.toBeNull();   // selecting still works: the next column is read-only too

    host.querySelector<HTMLButtonElement>('[drawerFooter] button, footer button')!.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.closedCount).toBe(1);
  });
});

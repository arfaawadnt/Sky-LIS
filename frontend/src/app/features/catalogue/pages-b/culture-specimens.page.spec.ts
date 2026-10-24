import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { CATALOGUE_A_API, type SampleTypeListItem } from '../../../core/api/catalogue-a.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { CultureSpecimensPage } from './culture-specimens.page';
import { MICROBIOLOGY_TABS } from './microbiology.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const urine: SampleTypeListItem = { id: 's3', code: 'URINE', shortName: 'UR', nameEn: 'Urine', nameAr: 'بول', activeConditionCount: 0, isActive: true, forCulture: true };
const swab: SampleTypeListItem = { id: 's2', code: 'WSWAB', shortName: 'SWAB', nameEn: 'Wound swab', nameAr: 'مسحة جرح', activeConditionCount: 0, isActive: false, forCulture: true };

/** LIS-CR-007 (C-11): Microbiology › Culture specimens, the sixth tab (CP-56). */
describe('CultureSpecimensPage (CP-56, LIS-CR-007)', () => {
  let backend: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'catalogue', children: [{ path: 'microbiology', children: [{ path: 'culture-specimens', component: CultureSpecimensPage }] }] }]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  it('is the last of the six tabs, after Sensitivity values', () => {
    expect(MICROBIOLOGY_TABS).toEqual(['organisms', 'antibiotics', 'drug-types', 'commercial-names', 'sensitivity-values', 'culture-specimens']);
  });

  it('lists the culture specimens only (code, short name, name, status) read-only, with the link to Sample types', async () => {
    await harness.navigateByUrl('/catalogue/microbiology/culture-specimens', CultureSpecimensPage);
    TestBed.tick();
    await settle();

    const request = backend.expectOne((candidate) => candidate.method === 'GET' && candidate.url === CATALOGUE_A_API.sampleTypes);
    expect(request.request.params.get('forCulture')).toBe('true');
    expect(request.request.params.get('isActive')).toBeNull();   // active and inactive: the status column shows which
    request.flush({ items: [urine, swab], page: 1, pageSize: 200, totalCount: 2, totalPages: 1 });
    await settle();
    TestBed.tick();

    const root = harness.routeNativeElement as HTMLElement;
    const text = root.textContent ?? '';
    expect(text).toContain('URINE');
    expect(text).toContain('Wound swab');
    expect(text).toContain('SWAB');
    expect(root.querySelectorAll('app-badge').length).toBeGreaterThanOrEqual(2);

    const manage = root.querySelector<HTMLAnchorElement>('a[href]');
    expect(manage?.getAttribute('href')).toBe('/catalogue/sample-types');
    expect(manage?.textContent).toContain('catalogue-b.cultureSpecimens.manage');

    // read-only: no add, edit or activation affordance
    expect(root.querySelector('button.btn.primary')).toBeNull();
    expect(root.querySelector('.row-btn')).toBeNull();
  });
});

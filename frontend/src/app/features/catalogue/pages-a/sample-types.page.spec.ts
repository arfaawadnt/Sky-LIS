import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_A_API, type SampleTypeListItem, type SampleTypeRequest } from '../../../core/api/catalogue-a.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import type { FieldSchema } from '../../../shared/ui/master-data-page/master-data.types';
import { SampleTypesPage } from './sample-types.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const serum: SampleTypeListItem = { id: 's1', code: 'SERUM', shortName: 'SER', nameEn: 'Serum', nameAr: 'مصل', activeConditionCount: 1, isActive: true, forCulture: false };
const swab: SampleTypeListItem = { id: 's2', code: 'WSWAB', shortName: 'SWAB', nameEn: 'Wound swab', nameAr: 'مسحة جرح', activeConditionCount: 0, isActive: true, forCulture: true };

/** The parts of the page the scenarios read (the page keeps them protected). */
interface PageInternals {
  readonly config: {
    readonly columns: readonly TableColumn<SampleTypeListItem>[];
    readonly fields: readonly FieldSchema[];
    readonly adapter: {
      create(dto: SampleTypeRequest): Promise<unknown>;
      update(id: string, dto: SampleTypeRequest): Promise<unknown>;
    };
  };
}

/** LIS-CR-007 (C-11): the sample-type editor (CP-43) carries the switch "Used for cultures". */
describe('SampleTypesPage: culture specimens (LIS-CR-007)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SampleTypesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function open(): Promise<{ root: HTMLElement; internals: PageInternals }> {
    const fixture = TestBed.createComponent(SampleTypesPage);
    TestBed.tick();
    await settle();
    backend.match((candidate) => candidate.method === 'GET' && candidate.url === CATALOGUE_A_API.sampleTypes).forEach((candidate) => candidate.flush({ items: [serum, swab], page: 1, pageSize: 25, totalCount: 2, totalPages: 1 }));
    await settle();
    TestBed.tick();
    return { root: fixture.nativeElement as HTMLElement, internals: fixture.componentInstance as unknown as PageInternals };
  }

  it('offers the switch in the editor, off for a new sample type', async () => {
    const { root, internals } = await open();
    expect(internals.config.fields.find((field) => field.name === 'forCulture')).toEqual(expect.objectContaining({ kind: 'boolean', defaultValue: false }));

    root.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    TestBed.tick();
    await settle();
    TestBed.tick();

    const dialog = root.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();
    const toggle = Array.from(dialog?.querySelectorAll<HTMLInputElement>('label.boolean input[type="checkbox"]') ?? []).find((input) => input.closest('label')?.textContent?.includes('catalogue-a.sampleTypes.field.forCulture'));
    expect(toggle).not.toBeNull();
    expect(toggle?.checked).toBe(false);
    expect(toggle?.closest('label')?.textContent).toContain('catalogue-a.sampleTypes.field.forCulture');
  });

  it('sends the flag on create and update, and marks the culture specimens of the list', async () => {
    const { internals } = await open();

    const creating = internals.config.adapter.create({ code: 'URINE', shortName: 'UR', nameEn: 'Urine', nameAr: 'بول', forCulture: true });
    const create = backend.expectOne((candidate) => candidate.method === 'POST' && candidate.url === CATALOGUE_A_API.sampleTypes);
    expect(create.request.body).toEqual(expect.objectContaining({ code: 'URINE', forCulture: true, conditions: [] }));
    create.flush({ id: 's3' }, { status: 201, statusText: 'Created' });
    await creating;

    const updating = internals.config.adapter.update('s2', { code: 'WSWAB', shortName: 'SWAB', nameEn: 'Wound swab', nameAr: 'مسحة جرح', forCulture: false });
    const update = backend.expectOne((candidate) => candidate.method === 'PUT' && candidate.url === `${CATALOGUE_A_API.sampleTypes}/s2`);
    expect(update.request.body).toEqual(expect.objectContaining({ forCulture: false }));
    update.flush(null, { status: 204, statusText: 'No Content' });
    await updating;

    const column = internals.config.columns.find((candidate) => candidate.key === 'forCulture');
    expect([column?.value?.(swab), column?.value?.(serum)]).toEqual(['catalogue-a.sampleTypes.culture', '']);
  });
});

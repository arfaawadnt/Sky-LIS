import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, type TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ANNOUNCEMENT_API, type AnnouncementView } from '../../../core/api/announcement.contracts';
import { PLATFORM_API, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { AnnouncementsFacade } from '../state/announcements.facade';
import { AnnouncementsPage } from './announcements.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
const page = <T>(items: readonly T[]): object => ({ items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 });

const OFFER: AnnouncementView = {
  id: 'a1', titleEn: 'Spring offer', titleAr: 'عرض الربيع', bodyEn: 'Ten percent off.', bodyAr: null, linkUrl: 'https://nationaltechnology.net/offer',
  placement: 'SignIn', fromUtc: '2026-10-01T00:00:00Z', toUtc: null, audienceCountryId: null, audiencePlanId: null, audienceStatus: 'Any',
  audienceClientIds: ['c2'], isActive: true, isLive: true, imageVersion: null, impressions: 12, clicks: 3, createdAtUtc: '2026-10-01T00:00:00Z',
};

interface PageApi {
  openEdit(announcement: AnnouncementView | null): void;
  save(): Promise<void>;
  toggle(row: AnnouncementView): Promise<void>;
  onImageSelected(event: Event): Promise<void>;
  form(): { controls: Record<string, { setValue(value: unknown): void }> } | null;
  imageError(): string | null;
}

/**
 * SA-22 Announcements (LIS-CR-013, C-17): the register with its states and audience, the editor's request (UTC schedule, empty
 * fields as null, the ticked clients), a new announcement staying open for its image, the image size check before upload and the
 * read-only view of a `platform.view` user.
 */
describe('Announcements page (SA-22)', () => {
  let backend: HttpTestingController;

  async function open(canManage = true): Promise<{ view: PageApi; root: HTMLElement }> {
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        AnnouncementsFacade,
        { provide: PermissionsService, useValue: { can: (key: string) => key !== PLATFORM_PERMISSIONS.platformManage || canManage } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();

    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AnnouncementsPage);
    TestBed.tick();
    await settle();
    flushLists();
    await settle();
    TestBed.tick();
    flushLists();   // a resource that started after the first answer
    await settle();
    fixture.detectChanges();
    return { view: fixture.componentInstance as unknown as PageApi, root: fixture.nativeElement as HTMLElement };
  }

  function flushLists(): void {
    backend.match((request) => request.url === ANNOUNCEMENT_API.admin && request.method === 'GET').forEach((request) => request.flush(page([OFFER])));
    backend.match(PLATFORM_API.countries).forEach((request) => request.flush(page([{ id: 'eg', nameEn: 'Egypt', nameAr: 'مصر' }])));
    backend.match((request) => request.url === PLATFORM_API.plans).forEach((request) => request.flush(page([{ id: 'p1', code: 'STARTER', nameEn: 'Starter', nameAr: 'أساسية' }])));
    backend.match((request) => request.url === PLATFORM_API.clients).forEach((request) => request.flush(page([
      { id: 'c1', nameEn: 'Al Noor Lab', nameAr: 'مختبر النور', portalSlug: 'al-noor' },
      { id: 'c2', nameEn: 'Nile Lab', nameAr: 'مختبر النيل', portalSlug: 'nile-lab' },
    ])));
  }

  afterEach(() => {
    TestBed.resetTestingModule();
    document.body.innerHTML = '';
  });

  it('lists the announcements with their placement, state, audience and counters', async () => {
    const { root } = await open();
    const text = root.textContent ?? '';
    expect(text).toContain('Spring offer');
    expect(text).toContain('platform.announcements.placement.SignIn');
    expect(text).toContain('platform.announcements.state.Live');
    expect(text).toContain('12');
  });

  it('a new announcement posts the UTC schedule, nulls for empty texts and the ticked clients, then stays open for its image', async () => {
    const { view, root } = await open();
    view.openEdit(null);
    TestBed.tick();
    await settle();
    const form = view.form()!;
    form.controls['titleEn']!.setValue('Autumn offer');
    form.controls['titleAr']!.setValue('عرض الخريف');
    form.controls['from']!.setValue('2026-11-01T08:00');
    form.controls['clientIds']!.setValue(['c1']);
    TestBed.tick();

    const saving = view.save();
    await settle();
    const post: TestRequest = backend.expectOne({ method: 'POST', url: ANNOUNCEMENT_API.admin });
    expect(post.request.body).toMatchObject({
      titleEn: 'Autumn offer', titleAr: 'عرض الخريف', bodyEn: null, bodyAr: null, linkUrl: null, placement: 'SignIn',
      fromUtc: new Date('2026-11-01T08:00').toISOString(), toUtc: null, audienceCountryId: null, audiencePlanId: null, audienceStatus: 'Any', audienceClientIds: ['c1'],
    });
    post.flush({ id: 'a2' }, { status: 201, statusText: 'Created' });
    await settle();
    flushLists();
    backend.expectOne(ANNOUNCEMENT_API.adminOne('a2')).flush({ ...OFFER, id: 'a2', titleEn: 'Autumn offer', isActive: false, isLive: false });
    await saving;
    TestBed.tick();
    await settle();

    expect(document.body.textContent).toContain('platform.announcements.image.upload');   // the drawer stayed open, now with the image block
    expect(root).toBeTruthy();
  });

  it('refuses an http link and a missing title before anything is sent', async () => {
    const { view } = await open();
    view.openEdit(null);
    TestBed.tick();
    const form = view.form()!;
    form.controls['titleEn']!.setValue('Offer');
    form.controls['linkUrl']!.setValue('http://example.com');
    await view.save();
    backend.expectNone({ method: 'POST', url: ANNOUNCEMENT_API.admin });
  });

  it('an image above 512 KB is refused in the browser (ANN-007)', async () => {
    const { view } = await open();
    view.openEdit(OFFER);
    TestBed.tick();
    const file = new File([new Uint8Array(512 * 1024 + 1)], 'big.png', { type: 'image/png' });
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [file] });
    await view.onImageSelected({ target: input } as unknown as Event);
    expect(view.imageError()).toBe('platform.announcements.error.ANN-007');
    backend.expectNone(ANNOUNCEMENT_API.adminImage('a1'));
  });

  it('a platform.view user opens a read-only announcement without Save', async () => {
    const { view } = await open(false);
    view.openEdit(OFFER);
    TestBed.tick();
    await settle();
    const fields = Array.from(document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>('#platform-announcement-form input, #platform-announcement-form textarea, #platform-announcement-form select'))
      .filter((field) => field.type !== 'search');
    expect(fields.length).toBeGreaterThan(0);
    expect(fields.every((field) => field.disabled)).toBe(true);
    expect(document.querySelector('button[type="submit"][form="platform-announcement-form"]')).toBeNull();
    expect(document.body.textContent).toContain('platform.announcements.view');
  });
});

/** LIS-CR-013: both dictionaries carry every announcements key (apart from the Arabic-only counted forms), the Arabic in Arabic. */
describe('Announcements dictionaries', () => {
  it('hold the same keys in English and Arabic', async () => {
    const en = (await import('../../../../../public/i18n/platform.en.json')).default as Record<string, string>;
    const ar = (await import('../../../../../public/i18n/platform.ar.json')).default as Record<string, string>;
    const ours = (dictionary: Record<string, string>): string[] => Object.keys(dictionary).filter((key) => key.startsWith('announcements.') && !/(Two|Many)$/.test(key)).sort();
    expect(ours(en).length).toBeGreaterThan(50);
    expect(ours(ar)).toEqual(ours(en));
    expect(ours(en).filter((key) => key.startsWith('announcements.error.') && !/[؀-ۿ]/.test(ar[key] ?? ''))).toEqual([]);
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { ANNOUNCEMENT_API, type PublicAnnouncement } from '../../../core/api/announcement.contracts';
import { AnnouncementDismissalsService } from '../state/announcement-dismissals.service';
import { WelcomeAnnouncementComponent } from './welcome-announcement.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const FIRST: PublicAnnouncement = { id: 'w1', titleEn: 'Welcome to Sky LIS', titleAr: 'مرحبًا بكم', bodyEn: 'Your trial has started.', bodyAr: 'بدأت تجربتكم.', linkUrl: 'https://nationaltechnology.net/start', imageVersion: 1 };
const SECOND: PublicAnnouncement = { id: 'w2', titleEn: 'Partner offer', titleAr: 'عرض شريك', bodyEn: null, bodyAr: null, linkUrl: null, imageVersion: null };

/**
 * LIS-CR-013 (C-17): the dashboard's welcome card — the Welcome announcements of the laboratory as one card, paged when there
 * are several, closed for the session (in memory, so a new visit of the dashboard keeps it closed), nothing when there is none.
 */
describe('Welcome announcement card (CP-05, LIS-CR-013)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function render(answer: readonly PublicAnnouncement[]): Promise<ComponentFixture<WelcomeAnnouncementComponent>> {
    const fixture = TestBed.createComponent(WelcomeAnnouncementComponent);
    fixture.detectChanges();
    TestBed.tick();
    await settle();
    backend.expectOne(ANNOUNCEMENT_API.mine).flush(answer);
    await settle();
    TestBed.tick();
    await settle();
    fixture.detectChanges();
    return fixture;
  }

  it('shows the first announcement, counts its impression, pages to the next one and counts a click', async () => {
    const fixture = await render([FIRST, SECOND]);
    const root = fixture.nativeElement as HTMLElement;
    backend.expectOne(ANNOUNCEMENT_API.impression('w1')).flush(null);
    expect(root.textContent).toContain('Welcome to Sky LIS');
    expect(root.querySelector('img')?.getAttribute('src')).toBe(ANNOUNCEMENT_API.publicImage('w1', 1));

    const link = root.querySelector<HTMLAnchorElement>('a[target=_blank]');
    link?.addEventListener('click', (event) => event.preventDefault());
    link?.click();
    backend.expectOne(ANNOUNCEMENT_API.click('w1')).flush(null);

    (Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((button) => button.getAttribute('aria-label') === 'account.announcement.next'))?.click();
    TestBed.tick();
    await settle();
    fixture.detectChanges();
    backend.expectOne(ANNOUNCEMENT_API.impression('w2')).flush(null);
    expect(root.textContent).toContain('Partner offer');
    expect(root.querySelector('img')).toBeNull();
  });

  it('closing the card hides it for the session, also on the next visit of the dashboard', async () => {
    const fixture = await render([FIRST]);
    backend.expectOne(ANNOUNCEMENT_API.impression('w1')).flush(null);
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('button.close')?.click();
    TestBed.tick();
    fixture.detectChanges();
    expect(root.querySelector('aside')).toBeNull();
    expect(TestBed.inject(AnnouncementDismissalsService).dismissed().has('w1')).toBe(true);

    const again = await render([FIRST]);
    expect((again.nativeElement as HTMLElement).querySelector('aside')).toBeNull();
  });

  it('shows nothing when the laboratory has no announcement (none, opted out, or the read failed)', async () => {
    const fixture = await render([]);
    expect((fixture.nativeElement as HTMLElement).querySelector('aside')).toBeNull();
  });
});

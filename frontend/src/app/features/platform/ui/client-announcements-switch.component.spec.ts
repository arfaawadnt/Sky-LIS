import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { ANNOUNCEMENT_API } from '../../../core/api/announcement.contracts';
import { PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ClientAnnouncementsSwitchComponent } from './client-announcements-switch.component';
import { announcementState, fromLocalInput, toLocalInput } from './announcement-time';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** SA-04 "Hide announcements for this client" (LIS-CR-013, decision D-4): read on open, saved at once, `tenancy.edit` only. */
describe('Client announcements switch (SA-04)', () => {
  let backend: HttpTestingController;

  async function render(canEdit: boolean, hidden: boolean): Promise<ComponentFixture<ClientAnnouncementsSwitchComponent>> {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => key !== PLATFORM_PERMISSIONS.tenancyEdit || canEdit } satisfies Pick<PermissionsService, 'can'> },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClientAnnouncementsSwitchComponent);
    fixture.componentRef.setInput('clientId', 'c1');
    fixture.detectChanges();
    TestBed.tick();
    await settle();
    backend.expectOne(ANNOUNCEMENT_API.clientSetting('c1')).flush({ hidden });
    await settle();
    TestBed.tick();
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    backend.verify();
    TestBed.resetTestingModule();
  });

  it('shows the stored value and saves a change at once', async () => {
    const fixture = await render(true, false);
    const box = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=checkbox]')!;
    expect(box.checked).toBe(false);
    expect(box.disabled).toBe(false);

    box.click();
    await settle();
    const put = backend.expectOne({ method: 'PUT', url: ANNOUNCEMENT_API.clientSetting('c1') });
    expect(put.request.body).toEqual({ hidden: true });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    TestBed.tick();
    fixture.detectChanges();
    expect(box.checked).toBe(true);
  });

  it('a user without tenancy.edit sees the value but cannot change it', async () => {
    const fixture = await render(false, true);
    const box = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=checkbox]')!;
    expect(box.checked).toBe(true);
    expect(box.disabled).toBe(true);
  });
});

/** The schedule fields of SA-22 and the state of an announcement (LIS-CR-013). */
describe('Announcement schedule helpers', () => {
  it('turns a local datetime-local value into UTC and back', () => {
    const iso = fromLocalInput('2026-11-01T08:30');
    expect(iso).toBe(new Date('2026-11-01T08:30').toISOString());
    expect(toLocalInput(iso)).toBe('2026-11-01T08:30');
    expect(fromLocalInput('')).toBeNull();
    expect(fromLocalInput('not a date')).toBeNull();
    expect(toLocalInput(null)).toBe('');
  });

  it('tells live, scheduled, ended and inactive announcements apart', () => {
    const now = new Date('2026-10-24T09:00:00Z');
    expect(announcementState({ isActive: true, fromUtc: '2026-10-01T00:00:00Z', toUtc: null }, now)).toBe('Live');
    expect(announcementState({ isActive: true, fromUtc: '2026-11-01T00:00:00Z', toUtc: null }, now)).toBe('Scheduled');
    expect(announcementState({ isActive: true, fromUtc: '2026-10-01T00:00:00Z', toUtc: '2026-10-24T09:00:00Z' }, now)).toBe('Ended');
    expect(announcementState({ isActive: false, fromUtc: '2026-10-01T00:00:00Z', toUtc: null }, now)).toBe('Inactive');
  });
});

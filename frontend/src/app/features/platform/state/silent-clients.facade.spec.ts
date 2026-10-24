import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PLATFORM_API, type SilentClientList } from '../../../core/api/platform.contracts';
import { SilentClientsFacade } from './silent-clients.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const answer = (days: number): SilentClientList => ({
  days,
  asOfUtc: '2026-10-24T09:00:00Z',
  trial: 1,
  items: [
    {
      id: 't1', nameEn: 'Quiet Lab', nameAr: 'معمل هادئ', portalUrl: 'https://lis.example.test/lab/quiet', status: 'Trial', createdAtUtc: '2026-09-01T09:00:00Z',
      lastSignInAtUtc: null, lastRegistrationAtUtc: null, lastActivityAtUtc: null, silentDays: 53, administratorName: 'Dr. Mona', administratorMobile: '+201112223333',
    },
  ],
});

describe('SilentClientsFacade (LIS-CR-007 A-01)', () => {
  let backend: HttpTestingController;
  let facade: SilentClientsFacade;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), SilentClientsFacade],
    });
    backend = TestBed.inject(HttpTestingController);
    facade = TestBed.inject(SilentClientsFacade);
  });

  it('reads nothing before the window is set, then the window and again when it changes', async () => {
    TestBed.tick();
    await settle();
    backend.expectNone((request) => request.url === PLATFORM_API.silentClients);
    facade.setDays(3);
    TestBed.tick();
    await settle();
    backend.expectOne(`${PLATFORM_API.silentClients}?days=3`).flush(answer(3));
    await settle();
    expect(facade.rows().map((row) => row.id)).toEqual(['t1']);
    expect(facade.trial()).toBe(1);

    facade.setDays(30);
    TestBed.tick();
    await settle();
    backend.expectOne(`${PLATFORM_API.silentClients}?days=30`).flush({ ...answer(30), trial: 0, items: [] });
    await settle();
    expect(facade.rows()).toEqual([]);
    expect(facade.trial()).toBe(0);
    backend.verify();
  });

  it('exports the window in the reader language with the server file name', async () => {
    facade.setDays(3);
    TestBed.tick();
    await settle();
    backend.expectOne(`${PLATFORM_API.silentClients}?days=3`).flush(answer(3));

    const exporting = facade.export.execute();
    await settle();
    backend.expectOne(`${PLATFORM_API.silentClients}/export?days=3&language=en`)
      .flush(new Blob(['x'], { type: 'text/csv' }), { headers: { 'Content-Disposition': 'attachment; filename=silent-clients-3d.csv' } });
    const file = await exporting;
    expect(file?.fileName).toBe('silent-clients-3d.csv');
  });
});

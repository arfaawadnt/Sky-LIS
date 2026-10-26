import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { TRACKER_API } from '../../../core/api/tracker.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { TRACKER_TIMERS, TrackerFacade } from '../state/tracker.facade';
import { ManualTimers, settle, trackerItem, trackerPage, trackerServiceItem } from '../testing/tracker.fixtures';
import { TrackerPage } from './tracker.page';

describe('TrackerPage (LIS-CR-006, CP-69)', () => {
  let fixture: ComponentFixture<TrackerPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;
  let granted: Set<string>;

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  const visitCalls = (): ReturnType<HttpTestingController['match']> => backend.match((request) => request.url === TRACKER_API.visits);

  async function open(permissions: readonly string[]): Promise<void> {
    granted = new Set(permissions);
    await TestBed.configureTestingModule({
      imports: [TrackerPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: TRACKER_TIMERS, useValue: new ManualTimers() },
        TrackerFacade,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TrackerPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    backend
      .expectOne((request) => request.url === TRACKER_API.visits)
      .flush(
        trackerPage([
          trackerItem({ nextStep: 'TakePayment', payment: { status: 'Partial', patientShare: 200, paid: 50, remaining: 150 }, delayState: 'Late', minutesLeft: -35, urgent: true }),
          trackerItem({ visitId: 'visit-2', accession: 'ACC-2026-00002', samples: null, results: null, payment: null, nextStep: 'EnterResults' }),
          trackerItem({ visitId: 'visit-3', accession: 'ACC-2026-00003', nextStep: 'CollectSample', stage: 'AwaitingSample', patientName: 'VIP patient', patientNameAr: null, patientMasked: true }),
        ]),
      );
    await render();
  }

  function rows(): HTMLTableRowElement[] {
    return [...host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]')];
  }

  function buttonWith(selector: string, text: string): HTMLButtonElement {
    const button = [...host.querySelectorAll<HTMLButtonElement>(selector)].find((candidate) => candidate.textContent?.includes(text));
    if (!button) {
      throw new Error(`no ${selector} with ${text}`);
    }

    return button;
  }

  it('lists the visits with the row edge colour and the next step as a link to the page that does it', async () => {
    await open(['registration.view', 'results.view', 'billing.view']);

    expect(rows().length).toBe(3);
    expect(rows()[0]?.classList.contains('edge-late')).toBe(true);
    expect(rows()[1]?.classList.contains('edge-ok')).toBe(true);
    const steps = rows().map((row) => row.querySelector<HTMLAnchorElement>('app-tracker-next-step a')?.getAttribute('href'));
    expect(steps).toEqual(['/billing/payments?visitId=visit-1&returnTo=%2F', '/results/by-accession?accession=ACC-2026-00002&returnTo=%2F', '/registration/accession?visitId=visit-3&returnTo=%2F']);
    expect(rows()[0]?.querySelector('a.link')?.getAttribute('href')).toBe('/registration/accession?visitId=visit-1&returnTo=%2F');
    expect(rows()[0]?.textContent).toContain('tracker.urgent');
    expect(rows()[0]?.textContent).toContain('tracker.time.late');
    expect(rows()[2]?.textContent).toContain('tracker.vipMasked');
    expect(rows()[0]?.textContent).not.toContain('tracker.vipMasked');
  });

  it('shows the next step as text when the user may not open its page', async () => {
    await open(['registration.view']);

    expect(rows()[0]?.querySelector('app-tracker-next-step a')).toBeNull();
    expect(rows()[0]?.querySelector('app-tracker-next-step')?.textContent).toContain('tracker.next.TakePayment');
    expect(rows()[2]?.querySelector('app-tracker-next-step a')?.getAttribute('href')).toBe('/registration/accession?visitId=visit-3&returnTo=%2F');
  });

  it('reads "—" with the "Not in your rights" tooltip where the server left a section out', async () => {
    await open(['registration.view']);

    const missing = rows()[1]?.querySelectorAll<HTMLElement>('.na[title]') ?? [];
    expect(missing.length).toBe(3);
    expect([...missing].every((cell) => cell.getAttribute('title') === 'tracker.notInRights')).toBe(true);
    expect(rows()[0]?.querySelectorAll('.na[title]').length).toBe(0);
  });

  it('filters by stage from the strip and by attention from the chips, as pressed toggles', async () => {
    await open(['registration.view']);

    const tile = buttonWith('app-tracker-pipeline-strip button', 'tracker.stage.InLab');
    expect(tile.getAttribute('aria-pressed')).toBe('false');
    tile.click();
    await render();
    const byStage = backend.expectOne((request) => request.url === TRACKER_API.visits);
    expect(byStage.request.params.get('stage')).toBe('InLab');
    byStage.flush(trackerPage([]));
    await render();
    expect(buttonWith('app-tracker-pipeline-strip button', 'tracker.stage.InLab').getAttribute('aria-pressed')).toBe('true');
    expect(buttonWith('app-tracker-pipeline-strip button', 'tracker.stage.Registered').getAttribute('aria-pressed')).toBe('false');

    buttonWith('app-tracker-attention-chips button', 'tracker.attention.Panic').click();
    await render();
    const byAttention = backend.expectOne((request) => request.url === TRACKER_API.visits);
    expect([byAttention.request.params.get('attention'), byAttention.request.params.get('stage')]).toEqual(['Panic', 'InLab']);
    byAttention.flush(trackerPage([]));
    await render();
    expect(buttonWith('app-tracker-attention-chips button', 'tracker.attention.Panic').getAttribute('aria-pressed')).toBe('true');
  });

  it('switches to the board (one request per lane) and back through the pressed Table | Board toggle', async () => {
    await open(['registration.view']);

    buttonWith('app-tracker-layout-toggle button', 'tracker.layout.board').click();
    await render();
    const lanes = visitCalls();
    expect(lanes.length).toBe(5);
    lanes.forEach((call) => call.flush(trackerPage(call.request.params.get('stage') === 'InLab' ? [trackerItem()] : [], { totalCount: call.request.params.get('stage') === 'InLab' ? 21 : 0 })));
    await render();

    expect(buttonWith('app-tracker-layout-toggle button', 'tracker.layout.board').getAttribute('aria-pressed')).toBe('true');
    expect(host.querySelector('app-data-table')).toBeNull();
    expect(host.querySelectorAll('app-tracker-board section.lane').length).toBe(5);
    expect(host.querySelectorAll('app-tracker-visit-card').length).toBe(1);

    buttonWith('app-tracker-board button', 'tracker.board.more').click();
    await render();
    const table = backend.expectOne((request) => request.url === TRACKER_API.visits);
    expect(table.request.params.get('stage')).toBe('InLab');
    table.flush(trackerPage([trackerItem()]));
    await render();
    expect(host.querySelector('app-data-table')).not.toBeNull();
    expect(buttonWith('app-tracker-layout-toggle button', 'tracker.layout.table').getAttribute('aria-pressed')).toBe('true');
  });

  it('offers the Bench view as a link to the Worklist and the presets as pressed toggles', async () => {
    await open(['registration.view']);

    const bench = [...host.querySelectorAll<HTMLAnchorElement>('app-tracker-view-tabs a')];
    expect(bench.map((link) => link.getAttribute('href'))).toEqual(['/results/worklist']);
    buttonWith('app-tracker-view-tabs button', 'tracker.view.FrontDesk').click();
    await render();
    expect(backend.expectOne((request) => request.url === TRACKER_API.visits).request.params.get('view')).toBe('FrontDesk');
    expect(buttonWith('app-tracker-view-tabs button', 'tracker.view.FrontDesk').getAttribute('aria-pressed')).toBe('true');
  });

  // ---- LIS-CR-014 -------------------------------------------------------------------------------------------------------

  const actionLinks = (row: HTMLTableRowElement | undefined): (string | null)[] =>
    [...(row?.querySelectorAll<HTMLAnchorElement>('app-tracker-row-actions a') ?? [])].map((link) => link.getAttribute('href'));

  it('T1: offers the CP-10 row actions by permission, each a link carrying returnTo', async () => {
    await open(['registration.view', 'registration.edit', 'registration.cancel', 'billing.view', 'patients.view', 'samples.view', 'results.view']);

    expect(actionLinks(rows()[0])).toEqual([
      '/registration/accession?accession=ACC-2026-00001&returnTo=%2F',
      '/registration/visits/visit-1?returnTo=%2F',
      '/billing/payments?visitId=visit-1&returnTo=%2F',
      '/registration/accession?accession=ACC-2026-00001&tab=documents&returnTo=%2F',
      '/patients/pat-1?returnTo=%2F',
      '/registration/accession?accession=ACC-2026-00001&tab=samples&returnTo=%2F',
      '/results/by-accession?accession=ACC-2026-00001&returnTo=%2F',
      '/registration/accession?accession=ACC-2026-00001&tab=cancel&returnTo=%2F',
    ]);
  });

  it('T1: a read-only desk keeps Details and Documents only', async () => {
    await open(['registration.view']);

    expect(actionLinks(rows()[0])).toEqual(['/registration/accession?accession=ACC-2026-00001&returnTo=%2F', '/registration/accession?accession=ACC-2026-00001&tab=documents&returnTo=%2F']);
  });

  it('T3: the Visits | Services toggle asks for service rows and shows the service column', async () => {
    await open(['registration.view']);

    buttonWith('app-tracker-rows-toggle button', 'tracker.rows.services').click();
    await render();
    const call = backend.expectOne((request) => request.url === TRACKER_API.visits);
    expect([call.request.params.get('rows'), call.request.params.get('page')]).toEqual(['Services', '1']);
    call.flush(trackerPage([], { rows: 'Services', services: [trackerServiceItem(), trackerServiceItem({ lineId: 'line-2', serviceCode: 'CBC', serviceName: 'Blood count', stage: 'AwaitingSample' })], totalCount: 2 }));
    await render();

    expect(rows().length).toBe(2);
    expect(host.querySelector('thead')?.textContent).toContain('tracker.column.service');
    expect(rows()[0]?.textContent).toContain('GLU · Glucose');
    expect(rows()[1]?.textContent).toContain('tracker.rows.visitStage');   // the visit is in the lab, this line waits for its sample
    expect(buttonWith('app-tracker-rows-toggle button', 'tracker.rows.services').getAttribute('aria-pressed')).toBe('true');
    expect(TestBed.inject(Router).url).toBe('/?rows=services');
  });

  it('T5: writes the filter into the URL, carries it in returnTo and restores a URL that names a state', async () => {
    await open(['registration.view']);
    const router = TestBed.inject(Router);

    buttonWith('app-tracker-pipeline-strip button', 'tracker.stage.InLab').click();
    await render();
    backend.expectOne((request) => request.url === TRACKER_API.visits).flush(trackerPage([trackerItem()]));
    await render();
    expect(router.url).toBe('/?stage=InLab');
    expect(rows()[0]?.querySelector('a.link')?.getAttribute('href')).toBe('/registration/accession?visitId=visit-1&returnTo=%2F%3Fstage%3DInLab');

    // a return (or back / forward) to a URL naming another state restores it whole
    await router.navigateByUrl('/?stage=Ready&rows=services&sort=service&dir=desc&layout=table');
    await render();
    const restored = backend.expectOne((request) => request.url === TRACKER_API.visits).request.params;
    expect([restored.get('stage'), restored.get('rows'), restored.get('sort'), restored.get('direction')]).toEqual(['Ready', 'Services', 'service', 'desc']);
    expect(router.url).toBe('/?stage=Ready&rows=services&sort=service&dir=desc');   // rewritten clean (defaults dropped)
  });
});

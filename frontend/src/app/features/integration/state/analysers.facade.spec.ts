import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { INTEGRATION_API, INTEGRATION_LOOKUP_API, type SaveAnalyserRequest } from '../../../core/api/integration.contracts';
import { analyser, page, settle } from '../testing/integration.fixtures';
import { AnalysersFacade } from './analysers.facade';

describe('AnalysersFacade (CP-58)', () => {
  let facade: AnalysersFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), AnalysersFacade] });
    facade = TestBed.inject(AnalysersFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  function flushLookups(): void {
    backend.expectOne((request) => request.url === INTEGRATION_API.siteAgents).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_LOOKUP_API.clinicalGroups).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_LOOKUP_API.sampleTypes).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_API.analyserModels).flush([]);
  }

  it('lists with the filter, creates through POST and reloads', async () => {
    await runEffects();
    const first = backend.expectOne((request) => request.url === INTEGRATION_API.analysers);
    expect(first.request.params.has('search')).toBe(false);
    first.flush(page([analyser]));
    flushLookups();
    await runEffects();
    expect(facade.list.value()?.items[0]?.code).toBe('XN550');

    facade.setSearch('xn');
    facade.setActiveFilter(true);
    await runEffects();
    const filtered = backend.expectOne((request) => request.url === INTEGRATION_API.analysers);
    expect(filtered.request.params.get('search')).toBe('xn');
    expect(filtered.request.params.get('isActive')).toBe('true');
    filtered.flush(page([analyser]));

    const request = { code: 'NEW', nameEn: 'New', nameAr: 'جديد', mode: 'Unidirectional' } as SaveAnalyserRequest;
    const pending = facade.save.execute({ id: null, request });
    await settle();
    const post = backend.expectOne({ method: 'POST', url: INTEGRATION_API.analysers });
    expect(post.request.body).toEqual(request);
    post.flush({ id: 'an-new' }, { status: 201, statusText: 'Created' });
    expect(await pending).toBe('an-new');
    await runEffects();
    backend.expectOne((r) => r.url === INTEGRATION_API.analysers).flush(page([analyser]));
    backend.verify();
  });

  it('updates with PUT, toggles activation and reads the connection state', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === INTEGRATION_API.analysers).flush(page([analyser]));
    flushLookups();

    const update = facade.save.execute({ id: 'an-1', request: { code: 'XN550' } as SaveAnalyserRequest });
    await settle();
    backend.expectOne({ method: 'PUT', url: `${INTEGRATION_API.analysers}/an-1` }).flush(null, { status: 204, statusText: 'No Content' });
    expect(await update).toBe('an-1');
    await runEffects();
    backend.expectOne((r) => r.url === INTEGRATION_API.analysers).flush(page([analyser]));

    const deactivate = facade.setActive.execute({ id: 'an-1', active: false });
    await settle();
    backend.expectOne({ method: 'POST', url: `${INTEGRATION_API.analysers}/an-1/deactivate` }).flush(null, { status: 204, statusText: 'No Content' });
    await deactivate;
    await runEffects();
    backend.expectOne((r) => r.url === INTEGRATION_API.analysers).flush(page([analyser]));

    const state = facade.testConnection.execute('an-1');
    await settle();
    backend.expectOne(`${INTEGRATION_API.analysers}/an-1/connection`).flush({ analyserId: 'an-1', agentStatus: 'Online', linkStatus: 'Connected' });
    expect((await state)?.linkStatus).toBe('Connected');
    backend.verify();
  });

  it('surfaces a refusal as a problem with its code', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === INTEGRATION_API.analysers).flush(page([]));
    flushLookups();

    const pending = facade.save.execute({ id: null, request: { code: 'DUP' } as SaveAnalyserRequest });
    await settle();
    backend.expectOne({ method: 'POST', url: INTEGRATION_API.analysers }).flush({ code: 'INT-100', title: 'exists', status: 409 }, { status: 409, statusText: 'Conflict' });
    expect(await pending).toBeNull();
    expect(facade.save.problem()?.code).toBe('INT-100');
  });
});

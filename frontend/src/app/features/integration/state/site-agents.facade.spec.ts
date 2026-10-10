import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { INTEGRATION_API, INTEGRATION_LOOKUP_API } from '../../../core/api/integration.contracts';
import { agent, page, settle } from '../testing/integration.fixtures';
import { SiteAgentsFacade } from './site-agents.facade';

describe('SiteAgentsFacade (CP-61)', () => {
  let facade: SiteAgentsFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), SiteAgentsFacade] });
    facade = TestBed.inject(SiteAgentsFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function initial(): Promise<void> {
    await runEffects();
    backend.expectOne((request) => request.url === INTEGRATION_API.siteAgents).flush(page([agent]));
    backend.expectOne((request) => request.url === INTEGRATION_LOOKUP_API.branches).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_API.analysers).flush(page([]));
    backend.expectOne((request) => request.url === INTEGRATION_API.intake).flush(page([]));
    await runEffects();
  }

  it('creates an agent and hands back the secret shown once', async () => {
    await initial();
    const secret = { agentId: 'ag-2', credentialId: 'mc-2', clientId: 'lis-ff', clientSecret: 's3cr3t', expiresAtUtc: '2027-10-10T00:00:00Z' };

    const pending = facade.create.execute({ name: 'Branch 2', branchId: 'br-2', configJson: null });
    await settle();
    const post = backend.expectOne({ method: 'POST', url: INTEGRATION_API.siteAgents });
    expect(post.request.body).toEqual({ name: 'Branch 2', branchId: 'br-2', configJson: null });
    post.flush(secret, { status: 201, statusText: 'Created' });
    expect((await pending)?.clientSecret).toBe('s3cr3t');
    await runEffects();
    backend.expectOne((request) => request.url === INTEGRATION_API.siteAgents).flush(page([agent]));
    backend.verify();
  });

  it('loads the logs of the selected agent with the level and period filters', async () => {
    await initial();
    facade.select('ag-1');
    await runEffects();
    backend.expectOne(`${INTEGRATION_API.siteAgents}/ag-1`).flush({ ...agent, configJson: '{}', analysers: [] });
    backend.expectOne((request) => request.url === `${INTEGRATION_API.siteAgents}/ag-1/logs`).flush(page([]));

    facade.setLogFilter({ level: 'Warning', from: '2026-10-10T00:00:00.000Z' });
    await runEffects();
    const logs = backend.expectOne((request) => request.url === `${INTEGRATION_API.siteAgents}/ag-1/logs`);
    expect(logs.request.params.get('level')).toBe('Warning');
    expect(logs.request.params.get('from')).toBe('2026-10-10T00:00:00.000Z');
    expect(logs.request.params.has('to')).toBe(false);
    logs.flush(page([{ id: 'l1', agentId: 'ag-1', atUtc: '2026-10-10T08:00:00Z', level: 'Error', message: 'Exception: port closed', sampleNumber: null, analyserCode: 'XN550', receivedAtUtc: '2026-10-10T08:00:01Z' }]));
    await runEffects();
    expect(facade.logs.value()?.items[0]?.message).toBe('Exception: port closed');
  });

  it('filters the intake log by status and rotates, pushes and deactivates through their endpoints', async () => {
    await initial();
    facade.setIntakeFilter({ status: 'Unmapped' });
    await runEffects();
    const intake = backend.expectOne((request) => request.url === INTEGRATION_API.intake);
    expect(intake.request.params.get('status')).toBe('Unmapped');
    intake.flush(page([]));

    const rotate = facade.rotate.execute('ag-1');
    await settle();
    backend.expectOne({ method: 'POST', url: `${INTEGRATION_API.siteAgents}/ag-1/rotate-secret` }).flush({ agentId: 'ag-1', credentialId: 'mc', clientId: 'lis-1', clientSecret: 'new', expiresAtUtc: '' });
    expect((await rotate)?.clientSecret).toBe('new');

    const push = facade.push.execute('ag-1');
    await settle();
    backend.expectOne({ method: 'POST', url: `${INTEGRATION_API.siteAgents}/ag-1/push-config` }).flush(null, { status: 204, statusText: 'No Content' });
    await push;

    const deactivate = facade.deactivate.execute('ag-1');
    await settle();
    backend.expectOne({ method: 'POST', url: `${INTEGRATION_API.siteAgents}/ag-1/deactivate` }).flush(null, { status: 204, statusText: 'No Content' });
    await deactivate;
    await runEffects();
    backend.match((request) => request.url === INTEGRATION_API.siteAgents).forEach((request) => request.flush(page([agent])));
  });
});

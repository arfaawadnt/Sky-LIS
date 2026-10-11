import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { INTEGRATION_API } from '../../../core/api/integration.contracts';
import { mutation } from '../../../core/state/mutation';
import { settle } from '../testing/integration.fixtures';
import { IntegrationApi } from './integration.api';

/**
 * Validation 2026-10-11 (the C-02 pattern): a 204 answer has a null body; the API must resolve `undefined`, because a
 * `mutation()` reads `null` as a failure and the page would stay silent (no toast, dialog left open).
 */
describe('IntegrationApi 204 answers', () => {
  let api: IntegrationApi;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(IntegrationApi);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('resolves undefined for a PUT answered 204', async () => {
    const pending = api.setAutoSend('an-1', 'map-1', true);
    backend.expectOne({ method: 'PUT', url: `${INTEGRATION_API.analysers}/an-1/mappings/map-1/auto-send` }).flush(null, { status: 204, statusText: 'No Content' });
    await expect(pending).resolves.toBeUndefined();
  });

  it('resolves undefined for a POST answered 204', async () => {
    const pending = api.pushAgentConfig('ag-1');
    backend.expectOne({ method: 'POST', url: `${INTEGRATION_API.siteAgents}/ag-1/push-config` }).flush(null, { status: 204, statusText: 'No Content' });
    await expect(pending).resolves.toBeUndefined();
  });

  it('lets a mutation report success for a 204 answer', async () => {
    const deactivate = mutation((id: string) => api.deactivateAgent(id));
    const pending = deactivate.execute('ag-1');
    await settle();
    backend.expectOne({ method: 'POST', url: `${INTEGRATION_API.siteAgents}/ag-1/deactivate` }).flush(null, { status: 204, statusText: 'No Content' });
    expect(await pending).not.toBeNull();
    expect(deactivate.problem()).toBeNull();
  });
});

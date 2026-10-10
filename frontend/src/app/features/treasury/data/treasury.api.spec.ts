import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TREASURY_API } from '../../../core/api/treasury.contracts';
import { mutation } from '../../../core/state/mutation';
import { TreasuryApi } from './treasury.api';

describe('TreasuryApi transitions', () => {
  it('resolve to undefined on 204, so mutation() reports success instead of failure', async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(TreasuryApi);
    const backend = TestBed.inject(HttpTestingController);
    const approve = mutation((id: string) => api.approve(id, null));

    const outcome = approve.execute('req-1');
    backend.expectOne(`${TREASURY_API.requests}/req-1/approve`).flush(null, { status: 204, statusText: 'No Content' });

    expect(await outcome).toBeUndefined();
    expect(approve.problem()).toBeNull();
    backend.verify();
  });
});

import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { TrackingFacade } from '../state/tracking.facade';
import { problemBody, sampleDetail, settle, timeline } from '../testing/samples.fixtures';
import { TrackingPage } from './tracking.page';

describe('TrackingPage (CP-17)', () => {
  let fixture: ComponentFixture<TrackingPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TrackingPage],
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), TrackingFacade],
    }).compileComponents();

    fixture = TestBed.createComponent(TrackingPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
  });

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  it('opens the timeline of the route parameter with the stage durations', async () => {
    fixture.componentRef.setInput('sampleNumber', 'SMP-2026-00001');
    await render();
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Received'));
    await settle();
    backend.expectOne(SAMPLES_API.timeline('smp-1')).flush(timeline());
    await render();

    expect(host.querySelectorAll('app-sample-timeline li.step').length).toBe(3);
    expect(host.querySelectorAll('app-stat-card').length).toBe(3);
    expect(host.textContent).toContain('Technician');
    backend.verify();
  });

  it('shows the refusal text when the barcode is unknown', async () => {
    fixture.componentRef.setInput('sampleNumber', 'SMP-404');
    await render();
    backend.expectOne(SAMPLES_API.byNumber('SMP-404')).flush(problemBody('SMP-404', 404), { status: 404, statusText: 'Not Found' });
    await render();

    expect(host.querySelector('.lookup-problem')).not.toBeNull();
    expect(host.querySelector('app-sample-timeline')).toBeNull();
    backend.verify();
  });
});

import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { LabTrackerStage } from '../../../core/api/tracker.contracts';
import { trackerItem } from '../testing/tracker.fixtures';
import { type BoardLane, TrackerBoardComponent } from './tracker-board.component';

describe('TrackerBoardComponent (layout B · stage lanes)', () => {
  let fixture: ComponentFixture<TrackerBoardComponent>;
  let host: HTMLElement;
  let more: LabTrackerStage[];

  function lane(stage: LabTrackerStage, overrides: Partial<BoardLane> = {}): BoardLane {
    return { stage, items: [], totalCount: 0, loading: false, failed: false, ...overrides };
  }

  async function show(lanes: readonly BoardLane[]): Promise<void> {
    await TestBed.configureTestingModule({ imports: [TrackerBoardComponent], providers: [provideZonelessChangeDetection(), provideRouter([])] }).compileComponents();
    fixture = TestBed.createComponent(TrackerBoardComponent);
    fixture.componentRef.setInput('lanes', lanes);
    more = [];
    fixture.componentInstance.more.subscribe((stage) => more.push(stage));
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  it('shows one lane per stage with its cards and a "+ n more" that asks for the table of that stage', async () => {
    await show([
      lane('AwaitingSample'),
      lane('InLab', { items: [trackerItem(), trackerItem({ visitId: 'visit-2', accession: 'ACC-2026-00002' })], totalCount: 23 }),
      lane('Ready', { failed: true }),
    ]);

    const lanes = host.querySelectorAll<HTMLElement>('section.lane');
    expect(lanes.length).toBe(3);
    expect(lanes[0]?.textContent).toContain('tracker.board.empty');
    expect(lanes[1]?.querySelectorAll('app-tracker-visit-card').length).toBe(2);
    expect(lanes[2]?.querySelector('[role="alert"]')).not.toBeNull();
    expect(lanes[0]?.querySelector('button')).toBeNull();

    lanes[1]?.querySelector<HTMLButtonElement>('button.more')?.click();
    expect(more).toEqual(['InLab']);
  });

  it('gives each card a four-segment progress bar with an accessible name, and the flag of the visit', async () => {
    await show([
      lane('InLab', {
        items: [trackerItem({ results: { total: 2, ordered: 0, complete: 1, reviewed: 0, verified: 1, printed: 0, hidden: 0, panic: 1, delta: 0, firstPanicTest: 'K', firstPanicTestAr: null }, payment: null })],
        totalCount: 1,
      }),
    ]);

    const bar = host.querySelector<HTMLElement>('app-tracker-visit-card .bar');
    expect(bar?.getAttribute('role')).toBe('img');
    expect(bar?.getAttribute('aria-label')).toBe('tracker.progress.label');
    expect([...(bar?.querySelectorAll('.seg') ?? [])].map((segment) => segment.className.replace('seg ', ''))).toEqual(['done', 'done', 'partial', 'unknown']);
    expect(host.querySelector('app-tracker-visit-card')?.textContent).toContain('tracker.flag.panic');
    expect(host.querySelector('app-tracker-visit-card a')?.getAttribute('href')).toBe('/registration/accession?visitId=visit-1');
  });

  it('tags a card "ext" when a sample of the visit was collected outside the laboratory (LIS-CR-008 C-02)', async () => {
    const samples = { total: 2, ordered: 0, collected: 1, received: 1, rejected: 0, firstRejectionReason: null, firstRejectionReasonAr: null };
    await show([lane('InLab', { items: [trackerItem({ samples: { ...samples, collectedOutside: 1 } }), trackerItem({ visitId: 'visit-2', samples: { ...samples, collectedOutside: 0 } }), trackerItem({ visitId: 'visit-3', samples: null })], totalCount: 3 })]);

    const cards = host.querySelectorAll('app-tracker-visit-card');
    expect(cards[0]?.textContent).toContain('tracker.samples.ext');
    expect(cards[1]?.textContent).not.toContain('tracker.samples.ext');
    expect(cards[2]?.textContent).not.toContain('tracker.samples.ext');
  });
});

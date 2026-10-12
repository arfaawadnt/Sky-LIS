import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PaginatorComponent } from './paginator.component';

@Component({
  imports: [PaginatorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-paginator [page]="page()" [pageSize]="pageSize()" [totalCount]="total()" (pageChange)="changes.push($event)" />`,
})
class HostComponent {
  readonly page = signal(2);
  readonly pageSize = signal(10);
  readonly total = signal(1234);
  readonly changes: PageRequest[] = [];
}

describe('PaginatorComponent (shared/ui)', () => {
  beforeEach(async () => {
    // the dictionary of the footer, as the shared feature file holds it
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => {
      const url = String(input);
      const body = url.endsWith('shared.ar.json') ? { 'paginator.showing': 'عرض {from}–{to} من {total}' } : url.endsWith('shared.en.json') ? { 'paginator.showing': 'Showing {from}–{to} of {total}' } : {};
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    await TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    await TestBed.inject(I18nService).load('shared');
  });

  afterEach(() => vi.restoreAllMocks());

  async function render(): Promise<{ host: HTMLElement; fixture: ReturnType<typeof TestBed.createComponent<HostComponent>> }> {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    return { host: fixture.nativeElement as HTMLElement, fixture };
  }

  it('shows the range, the page numbers and the page sizes in Latin digits in English', async () => {
    const { host } = await render();
    expect(host.querySelector('.summary')?.textContent?.trim()).toBe('Showing 11–20 of 1,234');
    expect([...host.querySelectorAll('.pg.num')].map((button) => button.textContent?.trim())).toEqual(['1', '2', '3', '4', '5']);
    expect(host.querySelector('select option')?.textContent?.trim()).toMatch(/^\d+$/);
  });

  it('shows them in Arabic-Indic digits in Arabic (validation v0.5.0 R-03)', async () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('ar');
    await i18n.load('shared');
    const { host } = await render();

    expect(host.querySelector('.summary')?.textContent?.trim()).toBe('عرض ١١–٢٠ من ١٬٢٣٤');
    expect([...host.querySelectorAll('.pg.num')].map((button) => button.textContent?.trim())).toEqual(['١', '٢', '٣', '٤', '٥']);
    const sizes = [...host.querySelectorAll('select option')].map((option) => option.textContent?.trim() ?? '');
    expect(sizes.length).toBeGreaterThan(0);
    expect(sizes.every((size) => /^[٠-٩]+$/.test(size))).toBe(true);
    // the value sent back stays a number
    expect([...host.querySelectorAll<HTMLOptionElement>('select option')].every((option) => /^\d+$/.test(option.value))).toBe(true);
    i18n.setLanguage('en');
  });

  it('still emits plain page requests', async () => {
    const { host, fixture } = await render();
    host.querySelectorAll<HTMLButtonElement>('.pg.num')[2]?.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.changes).toEqual([{ page: 3, pageSize: 10 }]);
  });
});

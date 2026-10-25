import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import type { ReportDesign } from '../../../core/api/report-designs';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ContractReportDesignComponent } from './contract-report-design.component';

/** C7 of CP-34 (LIS-CR-010): the tier's report design override. */
describe('ContractReportDesignComponent', () => {
  let fixture: ComponentFixture<ContractReportDesignComponent>;
  let granted: Set<string>;

  async function render(permissions: readonly string[], design: ReportDesign | null): Promise<HTMLElement> {
    granted = new Set(permissions);
    await TestBed.configureTestingModule({
      imports: [ContractReportDesignComponent],
      providers: [provideZonelessChangeDetection(), { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> }],
    }).compileComponents();
    fixture = TestBed.createComponent(ContractReportDesignComponent);
    fixture.componentRef.setInput('design', design);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function choose(host: HTMLElement, value: string): void {
    const select = host.querySelector('select')!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('offers "not set" and the five designs, shows the saved one and saves a change', async () => {
    const host = await render(['pricing.view', 'pricing.edit'], 'Modern');
    const saved: (ReportDesign | null)[] = [];
    fixture.componentInstance.save.subscribe((design) => saved.push(design));

    const options = [...host.querySelectorAll('option')].map((option) => option.value);
    expect(options).toEqual(['', 'Classic', 'Modern', 'Compact', 'Letterhead', 'Cumulative']);
    expect(host.querySelector('select')!.value).toBe('Modern');
    const button = host.querySelector<HTMLButtonElement>('button')!;
    expect(button.disabled).toBe(true);   // nothing changed yet

    choose(host, 'Cumulative');
    expect(button.disabled).toBe(false);
    button.click();
    choose(host, '');
    button.click();

    expect(saved).toEqual(['Cumulative', null]);
  });

  it('shows no save button without pricing.edit', async () => {
    const host = await render(['pricing.view'], null);
    expect(host.querySelector('select')!.value).toBe('');
    expect(host.querySelector('button')).toBeNull();
  });
});

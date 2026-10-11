import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { DocumentFrameComponent } from './document-frame.component';

@Component({
  selector: 'app-frame-host',
  imports: [DocumentFrameComponent],
  template: `<app-document-frame labNameEn="Sky Lab" labNameAr="مختبر سكاي" [branch]="branch()" [branchEn]="branchEn()" [branchAr]="branchAr()" title="Receipt" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class HostComponent {
  readonly branch = signal<string | null>(null);
  readonly branchEn = signal<string | null>('Main Branch');
  readonly branchAr = signal<string | null>('الفرع الرئيسي');
}

describe('DocumentFrameComponent (bilingual letterhead)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }));

  async function sides(change?: (host: HostComponent) => void): Promise<{ en: string; ar: string }> {
    const fixture = TestBed.createComponent(HostComponent);
    change?.(fixture.componentInstance);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return {
      en: root.querySelector('.lab[lang="en"]')?.textContent?.trim() ?? '',
      ar: root.querySelector('.lab[lang="ar"]')?.textContent?.trim() ?? '',
    };
  }

  it('prints the Arabic branch name under the Arabic laboratory name and the English one under the English name (UAT A-10)', async () => {
    const { en, ar } = await sides();
    expect(en).toContain('Main Branch');
    expect(en).not.toContain('الفرع الرئيسي');
    expect(ar).toContain('الفرع الرئيسي');
    expect(ar).not.toContain('Main Branch');
  });

  it('falls back to the other language, then to the single branch line', async () => {
    const missingArabic = await sides((host) => host.branchAr.set(null));
    expect(missingArabic.ar).toContain('Main Branch');

    const single = await sides((host) => {
      host.branchEn.set(null);
      host.branchAr.set('  ');
      host.branch.set('MAIN · Main Branch');
    });
    expect(single.en).toContain('MAIN · Main Branch');
    expect(single.ar).toContain('MAIN · Main Branch');
  });
});

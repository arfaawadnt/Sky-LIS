import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';

import { ReturnLinkComponent, returnToPath, safeReturnTo } from './return-link.component';

@Component({ template: '<p class="page">page</p>' })
class PageComponent {}

@Component({
  imports: [ReturnLinkComponent, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-return-link [label]="label()" /><router-outlet />`,
})
class HostComponent {
  readonly label = signal<string | null>(null);
}

describe('safeReturnTo (LIS-CR-014 T5: in-app paths only)', () => {
  it('accepts an in-app path with its query', () => {
    expect(safeReturnTo('/tracker?day=2026-10-20&stage=Ready')).toBe('/tracker?day=2026-10-20&stage=Ready');
    expect(safeReturnTo('/registration/visits')).toBe('/registration/visits');
    expect(returnToPath('/tracker?day=2026-10-20#x')).toBe('/tracker');
  });

  it('refuses another host, a scheme, a relative path and odd characters', () => {
    for (const value of [null, undefined, '', 'tracker', 'https://evil.example/x', '//evil.example/x', '/\\evil.example', '/javascript:alert(1)', 'javascript:alert(1)', '/a b', '/a\nb', `/${'x'.repeat(2001)}`]) {
      expect(safeReturnTo(value)).toBeNull();
    }

    expect(returnToPath('//evil.example')).toBeNull();
  });
});

describe('ReturnLinkComponent (shared/ui, LIS-CR-014 T5)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([
          { path: 'tracker', component: PageComponent },
          { path: 'registration/accession', component: PageComponent },
        ]),
      ],
    }).compileComponents();
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
  });

  const link = (): HTMLAnchorElement | null => (fixture.nativeElement as HTMLElement).querySelector('a.return');

  async function open(url: string): Promise<void> {
    await router.navigateByUrl(url);
    await fixture.whenStable();
  }

  it('shows nothing without a returnTo, or with one that leaves the app', async () => {
    await open('/registration/accession?accession=A-1');
    expect(link()).toBeNull();

    await open('/registration/accession?accession=A-1&returnTo=' + encodeURIComponent('https://evil.example/'));
    expect(link()).toBeNull();

    await open('/registration/accession?accession=A-1&returnTo=' + encodeURIComponent('//evil.example/'));
    expect(link()).toBeNull();
  });

  it('renders "Back to …" and navigates back to the list with its filter', async () => {
    const back = '/tracker?day=2026-10-20&stage=Ready&search=Mona%20Hassan';
    await open('/registration/accession?accession=A-1&returnTo=' + encodeURIComponent(back));
    expect(link()?.getAttribute('href')).toBe(back);
    expect(link()?.textContent).toContain('shared.returnLink.back');   // the generic label without a dictionary

    fixture.componentInstance.label.set('Lab Tracker');
    await fixture.whenStable();
    expect(link()?.getAttribute('aria-label')).toBe('shared.returnLink.backTo');

    link()?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    await fixture.whenStable();
    expect(router.url).toBe(back);
    expect(link()).toBeNull();   // back on the list: no chip
  });
});

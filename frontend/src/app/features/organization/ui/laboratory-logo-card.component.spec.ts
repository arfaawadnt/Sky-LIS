import { provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { ProblemError } from '../../../core/api/problem-details';
import { LaboratoryBrandService } from '../../../core/brand/laboratory-brand.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { LaboratoryLogoCardComponent } from './laboratory-logo-card.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('LaboratoryLogoCardComponent (Setup › Parameters)', () => {
  let granted: boolean;
  let brand: {
    logoUrl: ReturnType<typeof signal<string | null>>;
    initials: ReturnType<typeof signal<string>>;
    logoVersion: ReturnType<typeof signal<number | null>>;
    upload: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    granted = true;
    brand = {
      logoUrl: signal<string | null>(null),
      initials: signal('DL'),
      logoVersion: signal<number | null>(null),
      upload: vi.fn(() => Promise.resolve({ version: 1, contentType: 'image/png', sizeBytes: 4 })),
      remove: vi.fn(() => Promise.resolve()),
    };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: LaboratoryBrandService, useValue: brand },
        { provide: PermissionsService, useValue: { can: () => granted } satisfies Pick<PermissionsService, 'can'> },
      ],
    });
  });

  async function render(): Promise<ComponentFixture<LaboratoryLogoCardComponent>> {
    const fixture = TestBed.createComponent(LaboratoryLogoCardComponent);
    await fixture.whenStable();
    return fixture;
  }

  function choose(fixture: ComponentFixture<LaboratoryLogoCardComponent>, file: File): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=file]');
    if (!input) {
      throw new Error('no file input');
    }

    Object.defineProperty(input, 'files', { configurable: true, value: [file] });
    input.dispatchEvent(new Event('change'));
  }

  it('shows the initials until a logo exists and uploads the chosen image', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.logo-badge')?.textContent?.trim()).toBe('DL');
    expect(root.querySelector('label')?.textContent).toContain('organization.parameters.logo.upload');
    expect(root.querySelector('.btn.ghost')).toBeNull();

    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'logo.png', { type: 'image/png' });
    choose(fixture, file);
    await settle();
    expect(brand.upload).toHaveBeenCalledWith(file);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
  });

  it('shows the logo, offers replace and remove, and removes it', async () => {
    brand.logoUrl.set('blob:logo');
    brand.logoVersion.set(2);
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('img')?.getAttribute('src')).toBe('blob:logo');
    expect(root.querySelector('label')?.textContent).toContain('organization.parameters.logo.replace');

    root.querySelector<HTMLButtonElement>('.btn.ghost')?.click();
    await settle();
    expect(brand.remove).toHaveBeenCalled();
  });

  it('reports a refused image with an error toast', async () => {
    brand.upload.mockImplementation(() => Promise.reject(new ProblemError({ type: 'about:blank', title: 'Refused', status: 422, code: 'ACC-042', traceId: null, correlationId: null, errors: null }, 422)));
    const fixture = await render();
    choose(fixture, new File(['<svg/>'], 'logo.svg', { type: 'image/svg+xml' }));
    await settle();
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'error')).toBe(true);
  });

  it('shows the logo without any control to a user who cannot change it', async () => {
    granted = false;
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.logo-row')).not.toBeNull();
    expect(root.querySelector('input[type=file]')).toBeNull();
  });
});

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { LaboratoryBrandService } from '../core/brand/laboratory-brand.service';
import { PortalService } from '../core/portal/portal.service';

/**
 * The client laboratory's identity in the top bar (user request 2026-10-12): its logo, or a badge with its initials
 * until the laboratory uploads one on the profile page, and its name in the language of the page
 * (`LaboratoryBrandService`). The Sky LIS logo stays at the top of the sidebar. Nothing is shown in the Super Admin
 * portal (the platform has no laboratory). Links to the portal's home page.
 */
@Component({
  selector: 'app-shell-lab-brand',
  imports: [RouterLink],
  template: `
    <!-- a laboratory without a client record has no name: its logo alone is shown, or nothing -->
    @if (brand.name() !== null || brand.logoUrl() !== null) {
      <a class="lab" [routerLink]="portal.homePath()" [title]="brand.name() ?? ''">
        @if (brand.logoUrl(); as url) {
          <img class="logo" [src]="url" [alt]="brand.name() ?? ''" />
        } @else {
          <span class="badge" aria-hidden="true">{{ brand.initials() }}</span>
        }
        @if (brand.name(); as labName) {
          <span class="name">{{ labName }}</span>
        }
      </a>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      min-width: 0;
    }

    .lab {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      min-width: 0;
      color: var(--navy);
      text-decoration: none;
    }

    .logo {
      flex: none;
      max-width: 120px;
      height: 34px;
      object-fit: contain;
    }

    .badge {
      flex: none;
      display: inline-grid;
      place-items: center;
      width: 34px;
      height: 34px;
      border-radius: 9px;
      background: var(--grad);
      color: #fff;
      font-size: 13px;
      font-weight: 700;
      letter-spacing: 0.02em;
    }

    .name {
      max-width: 260px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-weight: 700;
      font-size: 15px;
    }

    /* under 960px the top bar keeps the logo or badge only, as it did with the product logo */
    @media (max-width: 959px) {
      .name {
        display: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabBrandComponent {
  protected readonly brand = inject(LaboratoryBrandService);
  protected readonly portal = inject(PortalService);
}

import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { ORGANIZATION_PERMISSIONS } from '../../../core/api/organization.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { LaboratoryBrandService } from '../../../core/brand/laboratory-brand.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

/**
 * The laboratory's logo on Setup › Parameters (user decision 2026-10-12: one logo per laboratory, a laboratory-wide
 * setting, not a personal one): the current image or the initials, and — for a holder of `settings.manage` — upload,
 * replace and remove. Every user of the laboratory sees the change in the top bar at once (`LaboratoryBrandService`).
 */
@Component({
  selector: 'app-laboratory-logo-card',
  imports: [SectionCardComponent, CanDirective],
  template: `
    <app-section-card [title]="i18n.t('organization.parameters.logo.title')" [subtitle]="i18n.t('organization.parameters.logo.subtitle')">
      <div class="logo-row">
        <div class="logo-box">
          @if (brand.logoUrl(); as url) {
            <img [src]="url" [alt]="i18n.t('organization.parameters.logo.title')" />
          } @else {
            <span class="logo-badge" aria-hidden="true">{{ brand.initials() || '—' }}</span>
          }
        </div>
        <div class="logo-text">
          <span>{{ i18n.t(brand.logoVersion() !== null ? 'organization.parameters.logo.set' : 'organization.parameters.logo.none') }}</span>
          <div class="logo-actions" *appCan="permissions.settingsManage">
            <label class="btn sm" [class.disabled]="busy()">
              <input class="visually-hidden" type="file" accept="image/png,image/jpeg,image/webp" [disabled]="busy()" (change)="onSelected($event)" />
              {{ i18n.t(brand.logoVersion() !== null ? 'organization.parameters.logo.replace' : 'organization.parameters.logo.upload') }}
            </label>
            @if (brand.logoVersion() !== null) {
              <button type="button" class="btn sm ghost" [disabled]="busy()" (click)="remove()">{{ i18n.t('organization.parameters.logo.remove') }}</button>
            }
            <span class="small muted">{{ i18n.t('organization.parameters.logo.hint') }}</span>
          </div>
        </div>
      </div>
    </app-section-card>
  `,
  styles: `
    .logo-row {
      display: flex;
      gap: var(--sp-4);
      align-items: center;
    }

    .logo-box {
      flex: none;
      display: grid;
      place-items: center;
      width: 120px;
      height: 72px;
      border: 1px dashed var(--line);
      border-radius: var(--r);
      background: var(--surface-2);
    }

    .logo-box img {
      max-width: 110px;
      max-height: 62px;
      object-fit: contain;
    }

    .logo-badge {
      display: grid;
      place-items: center;
      width: 46px;
      height: 46px;
      border-radius: 11px;
      background: var(--grad);
      color: #fff;
      font-weight: 700;
    }

    .logo-text {
      display: grid;
      gap: var(--sp-2);
      min-width: 0;
    }

    .logo-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-2);
      align-items: center;
    }

    .logo-actions label:has(input:focus-visible) {
      box-shadow: var(--focus-ring);
    }

    .logo-actions label.disabled {
      opacity: 0.6;
      pointer-events: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LaboratoryLogoCardComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly brand = inject(LaboratoryBrandService);
  private readonly toast = inject(ToastService);
  protected readonly permissions = ORGANIZATION_PERMISSIONS;
  protected readonly busy = signal(false);

  /** Uploads the chosen image; the server checks the type (by its bytes) and the size and answers with a coded refusal. */
  protected async onSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';   // the same file can be chosen again after a refusal
    if (file !== undefined && !this.busy()) {
      await this.run(() => this.brand.upload(file), 'organization.parameters.logo.saved');
    }
  }

  protected async remove(): Promise<void> {
    if (!this.busy()) {
      await this.run(() => this.brand.remove(), 'organization.parameters.logo.removed');
    }
  }

  private async run(action: () => Promise<unknown>, doneKey: string): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      this.toast.success(this.i18n.t(doneKey));
    } catch (error) {
      this.toast.error(error instanceof ProblemError ? problemMessage(this.i18n, 'organization', error.problem) : this.i18n.t('organization.parameters.logo.error'));
    } finally {
      this.busy.set(false);
    }
  }
}

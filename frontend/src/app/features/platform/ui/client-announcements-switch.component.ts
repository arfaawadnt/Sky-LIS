import { ChangeDetectionStrategy, Component, computed, inject, input, resource, signal } from '@angular/core';

import { PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { AnnouncementsApi } from '../data/announcements.api';

/**
 * SA-04 "Hide announcements for this client" (LIS-CR-013, decision D-4): the client's opt-out of the platform's announcements
 * (sign-in hero, welcome card). Saved at once, apart from the client form (its own endpoint); `tenancy.edit` changes it.
 */
@Component({
  selector: 'app-client-announcements-switch',
  template: `
    <label class="switch-row">
      <input type="checkbox" [checked]="hidden()" [disabled]="!canChange() || busy() || setting.isLoading() || setting.error() !== undefined" (change)="onToggle($event)" />
      <span>
        <strong>{{ i18n.t('platform.announcements.clientSwitch.label') }}</strong>
        <span class="small muted">{{ i18n.t('platform.announcements.clientSwitch.hint') }}</span>
      </span>
    </label>
  `,
  styles: `
    .switch-row {
      display: flex;
      gap: var(--sp-2);
      align-items: flex-start;
    }

    .switch-row > span {
      display: grid;
      gap: 2px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientAnnouncementsSwitchComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AnnouncementsApi);
  private readonly toast = inject(ToastService);
  private readonly access = inject(PermissionsService);

  readonly clientId = input.required<string>();

  protected readonly setting = resource({
    params: () => this.clientId(),
    loader: ({ params }) => this.api.clientSetting(params),
  });

  /** The value just saved, until the setting is read again. */
  private readonly saved = signal<{ readonly clientId: string; readonly hidden: boolean } | null>(null);

  protected readonly hidden = computed(() => {
    const saved = this.saved();
    if (saved !== null && saved.clientId === this.clientId()) {
      return saved.hidden;
    }

    return this.setting.hasValue() ? this.setting.value().hidden : false;
  });

  protected readonly canChange = computed(() => this.access.can(PLATFORM_PERMISSIONS.tenancyEdit));
  protected readonly busy = signal(false);

  protected async onToggle(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const hidden = input.checked;
    const clientId = this.clientId();
    this.busy.set(true);
    try {
      await this.api.setClientHidden(clientId, hidden);
      this.saved.set({ clientId, hidden });
      this.toast.success(this.i18n.t(hidden ? 'platform.announcements.clientSwitch.hidden' : 'platform.announcements.clientSwitch.shown'));
    } catch (error) {
      input.checked = !hidden;
      this.toast.error(error instanceof ProblemError ? problemMessage(this.i18n, 'platform', error.problem) : this.i18n.t('platform.loadError'));
    } finally {
      this.busy.set(false);
    }
  }
}

import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { ORGANIZATION_PERMISSIONS, PARAMETER_GROUPS, type ParameterDescriptor, type ParameterGroup } from '../../../core/api/organization.contracts';
import type { TenantSettingItem } from '../../../core/api/settings.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PortalService } from '../../../core/portal/portal.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ParametersFacade } from '../state/parameters.facade';

/** True when the text value satisfies the descriptor's input (mirrors the server's SET-002 check for the bounded inputs). */
export function isValidParameter(descriptor: ParameterDescriptor, value: string): boolean {
  switch (descriptor.input) {
    case 'switch':
      return value === 'true' || value === 'false';
    case 'number': {
      if (!/^-?\d+$/.test(value.trim())) {
        return false;
      }

      const number = Number(value);
      return (descriptor.min === undefined || number >= descriptor.min) && (descriptor.max === undefined || number <= descriptor.max);
    }
    case 'select':
      return (descriptor.options ?? []).includes(value);
    case 'text':
      return value.trim().length > 0 && value.length <= (descriptor.maxLength ?? 200);
  }
}

/**
 * Parameters (SRS CP-57): the tenant behaviour switches every module reads at runtime, grouped by topic with typed
 * inputs (switch, bounded number, closed list, text). Edits are collected and saved together (Save or Ctrl+S); only
 * changed keys are sent; a value can be returned to the platform default. Read-only without `settings.manage`.
 */
@Component({
  selector: 'app-parameters-page',
  imports: [SaveShortcutDirective, CanDirective, PageHeaderComponent, SectionCardComponent, BadgeComponent, IconComponent],
  templateUrl: './parameters.page.html',
  styleUrl: './parameters.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParametersPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ParametersFacade);
  private readonly toast = inject(ToastService);
  private readonly isPlatform = inject(PortalService).isPlatform;
  private readonly permissionsService = inject(PermissionsService);

  protected readonly permissions = ORGANIZATION_PERMISSIONS;
  protected readonly canManage = computed(() => this.permissionsService.can(ORGANIZATION_PERMISSIONS.settingsManage));

  /** The groups of this portal, restricted to the keys the server returned. */
  protected readonly groups = computed<readonly ParameterGroup[]>(() => {
    const known = this.facade.byKey();
    return PARAMETER_GROUPS.map((group) => ({
      id: group.id,
      parameters: group.parameters.filter((parameter) => known.has(parameter.key) && (this.isPlatform() || !parameter.platformOnly)),
    })).filter((group) => group.parameters.length > 0);
  });

  protected readonly invalidKeys = computed(() => {
    const descriptors = new Map(PARAMETER_GROUPS.flatMap((group) => group.parameters).map((parameter) => [parameter.key as string, parameter]));
    return new Set([...this.facade.edits()].filter(([key, value]) => {
      const descriptor = descriptors.get(key);
      return descriptor !== undefined && !isValidParameter(descriptor, value);
    }).map(([key]) => key));
  });

  constructor() {
    void this.i18n.load('organization');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected item(key: string): TenantSettingItem | undefined {
    return this.facade.byKey().get(key);
  }

  protected label(key: string): string {
    const label = `organization.parameters.key.${key}`;
    return this.i18n.has(label) ? this.i18n.t(label) : key;
  }

  protected description(key: string): string {
    const hint = `organization.parameters.hint.${key}`;
    return this.i18n.has(hint) ? this.i18n.t(hint) : (this.item(key)?.description ?? '');
  }

  protected optionLabel(key: string, option: string): string {
    const label = `organization.parameters.option.${key}.${option}`;
    return this.i18n.has(label) ? this.i18n.t(label) : option;
  }

  protected isEdited(key: string): boolean {
    return this.facade.edits().has(key);
  }

  protected onSwitch(key: string, event: Event): void {
    this.facade.set(key, (event.target as HTMLInputElement).checked ? 'true' : 'false');
  }

  protected onValue(key: string, event: Event): void {
    this.facade.set(key, (event.target as HTMLInputElement | HTMLSelectElement).value);
  }

  protected async save(): Promise<void> {
    if (!this.canManage() || !this.facade.dirty()) {
      return;
    }

    if (this.invalidKeys().size > 0) {
      this.toast.warn(this.i18n.t('organization.parameters.fixInvalid'));
      return;
    }

    if ((await this.facade.save.execute(this.facade.changes())) !== null) {
      this.toast.success(this.i18n.t('organization.parameters.saved'));
    }
  }

  /** Enter in a field saves like Ctrl+S; the native submission never reloads the page. */
  protected onSubmit(event: Event): void {
    event.preventDefault();
    void this.save();
  }

  protected problemText(): string {
    return problemMessage(this.i18n, 'organization', this.facade.save.problem());
  }
}

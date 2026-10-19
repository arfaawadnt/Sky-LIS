import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';

import { type LocationLevel, type LocationListItem, type LocationRequest, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { ProblemCodes, type ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { pick } from './format';

/** What the column emits on Save: a new row (`id` null) or the renamed one. */
export interface LocationColumnSave {
  readonly id: string | null;
  readonly request: LocationRequest;
}

/**
 * One column of the Locations drawer (SA-11b): the rows of a level under its parent, searched on the server, a row
 * selected by clicking it (the next column follows), and — with `tenancy.manage` — an inline editor to add or rename
 * a row and a button to activate / deactivate it. Dumb: the facade owns the data and the mutations.
 */
@Component({
  selector: 'app-platform-location-column',
  imports: [ReactiveFormsModule, SearchBoxComponent, LocalizedTextInputComponent, BadgeComponent, CanDirective, IconComponent],
  template: `
    <header class="col-head">
      <h3 class="col-title">{{ i18n.t('platform.locations.' + level()) }}</h3>
      @if (parentId() !== null) {
        <button type="button" class="btn sm" *appCan="permissionKeys.tenancyManage" [disabled]="pending()" (click)="startAdd()"><app-icon name="plus" [size]="14" />{{ i18n.t('platform.locations.add') }}</button>
      }
    </header>

    @if (parentId() === null) {
      <p class="muted small col-note">{{ i18n.t('platform.locations.pick.' + level()) }}</p>
    } @else {
      <app-search-box [placeholder]="i18n.t('platform.locations.search')" [label]="i18n.t('platform.locations.search')" (searchChange)="searchChange.emit($event)" />
      <label class="check small">
        <input type="checkbox" [checked]="showInactive()" (change)="onShowInactive($event)" />
        <span>{{ i18n.t('platform.locations.showInactive') }}</span>
      </label>

      @if (editing() !== undefined) {
        <form class="editor" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <app-localized-text-input [group]="form" [label]="i18n.t('platform.locations.name')" name="name" [maxLength]="100" [problem]="problem()" />
          @if (generalProblem(); as message) {
            <p class="problem small" role="alert">{{ message }}</p>
          }
          <div class="editor-actions">
            <button type="submit" class="btn primary sm" [disabled]="pending()">{{ i18n.t('platform.locations.save') }}</button>
            <button type="button" class="btn ghost sm" (click)="cancel()">{{ i18n.t('platform.locations.cancel') }}</button>
          </div>
        </form>
      } @else if (generalProblem(); as message) {
        <p class="problem small" role="alert">{{ message }}</p>
      }

      @if (error()) {
        <p class="problem small" role="alert">{{ i18n.t('platform.loadError') }}</p>
      } @else if (loading() && rows().length === 0) {
        <p class="muted small" role="status">{{ i18n.t('platform.loading') }}</p>
      } @else if (rows().length === 0) {
        <p class="muted small col-note">{{ i18n.t('platform.locations.empty') }}</p>
      } @else {
        <ul class="rows" [attr.aria-label]="i18n.t('platform.locations.' + level())">
          @for (row of rows(); track row.id) {
            <li class="row" [class.selected]="row.id === selectedId()" [class.inactive]="!row.isActive">
              <button type="button" class="row-name" [attr.aria-pressed]="selectable() ? row.id === selectedId() : null" [disabled]="!selectable()" (click)="selectRow.emit(row.id)">
                <span>{{ pick(row.nameEn, row.nameAr, i18n.lang()) }}</span>
                @if (!row.isActive) {
                  <app-badge tone="neutral">{{ i18n.t('platform.locations.inactive') }}</app-badge>
                }
              </button>
              <span class="row-actions" *appCan="permissionKeys.tenancyManage">
                <button type="button" class="row-btn" [disabled]="pending()" (click)="startRename(row)" [attr.aria-label]="i18n.t('platform.locations.rename')" [title]="i18n.t('platform.locations.rename')"><app-icon name="edit" [size]="14" /></button>
                <button type="button" class="row-btn" [class.danger]="row.isActive" [disabled]="pending()" (click)="toggleActive.emit(row)" [attr.aria-label]="i18n.t(row.isActive ? 'platform.locations.deactivate' : 'platform.locations.activate')" [title]="i18n.t(row.isActive ? 'platform.locations.deactivate' : 'platform.locations.activate')"><app-icon name="power" [size]="14" /></button>
              </span>
            </li>
          }
        </ul>
      }
    }
  `,
  styleUrl: './location-column.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationColumnComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly permissionKeys = PLATFORM_PERMISSIONS;
  protected readonly pick = pick;

  readonly level = input.required<LocationLevel>();
  /** The parent the rows belong to; null while the previous column has no selection. */
  readonly parentId = input<string | null>(null);
  readonly rows = input<readonly LocationListItem[]>([]);
  readonly loading = input(false);
  readonly error = input(false);
  readonly selectedId = input<string | null>(null);
  /** Areas end the chain: nothing to select below them. */
  readonly selectable = input(true);
  readonly showInactive = input(false);
  readonly pending = input(false);
  /** The refusal of the last save or toggle of this level (the facade's `activeLevel` decides which column gets it). */
  readonly problem = input<ProblemDetails | null>(null);

  readonly selectRow = output<string>();
  readonly searchChange = output<string>();
  readonly showInactiveChange = output<boolean>();
  readonly saveRow = output<LocationColumnSave>();
  readonly toggleActive = output<LocationListItem>();

  /** `undefined` = closed, `null` = adding, a row = renaming it. */
  protected readonly editing = signal<LocationListItem | null | undefined>(undefined);
  protected readonly form = localizedTextGroup({}, { maxLength: 100 });

  /** Shape refusals land on the fields through the localized input; the rest (GEO-015, GEO-10x, GEO-112) read here. */
  protected readonly generalProblem = computed(() => {
    const problem = this.problem();
    return problem === null || problem.code === ProblemCodes.validationFailed ? null : problemMessage(this.i18n, 'platform.locations', problem);
  });

  constructor() {
    // A new parent closes the editor: the rows below are another list.
    effect(() => {
      this.parentId();
      untracked(() => this.cancel());
    });
  }

  /** Closes the editor after a save the facade accepted (the host calls it: the row list reloads on its own). */
  saved(): void {
    this.cancel();
  }

  protected startAdd(): void {
    this.form.reset({ en: '', ar: '' });
    this.editing.set(null);
  }

  protected startRename(row: LocationListItem): void {
    this.form.reset({ en: row.nameEn, ar: row.nameAr });
    this.editing.set(row);
  }

  protected cancel(): void {
    this.editing.set(undefined);
  }

  protected submit(): void {
    const editing = this.editing();
    if (editing === undefined || this.pending() || !markAllTouched(this.form)) {
      return;
    }

    const value = this.form.getRawValue();
    this.saveRow.emit({ id: editing?.id ?? null, request: { nameEn: value.en.trim(), nameAr: value.ar.trim() } });
  }

  protected onShowInactive(event: Event): void {
    this.showInactiveChange.emit((event.target as HTMLInputElement).checked);
  }
}

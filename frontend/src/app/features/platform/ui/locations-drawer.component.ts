import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, untracked, viewChildren } from '@angular/core';

import type { GovernorateListItem, LocationLevel, LocationListItem } from '../../../core/api/platform.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { LocationsFacade } from '../state/locations.facade';
import { pick } from './format';
import { LocalityImportComponent } from './locality-import.component';
import { LocationColumnComponent, type LocationColumnSave } from './location-column.component';

/**
 * SA-11b "Locations" of a governorate (LIS-CR-003 §3): a drawer with three linked columns — cities → districts of the
 * selected city → areas of the selected district — each with search, add, rename and activate / deactivate
 * (`tenancy.manage`), and (LIS-CR-008 C-04) the CSV import of the whole hierarchy above the columns. Open while `governorate`
 * is set; closing emits `closed`. Owns its facade.
 */
@Component({
  selector: 'app-platform-locations-drawer',
  imports: [DrawerComponent, LocalityImportComponent, LocationColumnComponent],
  providers: [LocationsFacade],
  template: `
    <app-drawer [open]="governorate() !== null" (openChange)="onOpenChange($event)" [title]="title()" size="xl">
      @if (governorate() !== null) {
        <app-platform-locality-import />
        <div class="columns">
          <app-platform-location-column
            level="cities"
            [parentId]="facade.parentOf('cities')"
            [rows]="rowsOf('cities')"
            [loading]="facade.cities.isLoading()"
            [error]="facade.cities.error() !== undefined"
            [selectedId]="facade.cityId()"
            [showInactive]="facade.filters.cities().showInactive"
            [pending]="pending()"
            [problem]="problemOf('cities')"
            (selectRow)="facade.select('cities', $event)"
            (searchChange)="facade.setFilter('cities', { search: $event })"
            (showInactiveChange)="facade.setFilter('cities', { showInactive: $event })"
            (saveRow)="save('cities', $event)"
            (toggleActive)="toggle('cities', $event)"
          />
          <app-platform-location-column
            level="districts"
            [parentId]="facade.parentOf('districts')"
            [rows]="rowsOf('districts')"
            [loading]="facade.districts.isLoading()"
            [error]="facade.districts.error() !== undefined"
            [selectedId]="facade.districtId()"
            [showInactive]="facade.filters.districts().showInactive"
            [pending]="pending()"
            [problem]="problemOf('districts')"
            (selectRow)="facade.select('districts', $event)"
            (searchChange)="facade.setFilter('districts', { search: $event })"
            (showInactiveChange)="facade.setFilter('districts', { showInactive: $event })"
            (saveRow)="save('districts', $event)"
            (toggleActive)="toggle('districts', $event)"
          />
          <app-platform-location-column
            level="areas"
            [parentId]="facade.parentOf('areas')"
            [rows]="rowsOf('areas')"
            [loading]="facade.areas.isLoading()"
            [error]="facade.areas.error() !== undefined"
            [selectable]="false"
            [showInactive]="facade.filters.areas().showInactive"
            [pending]="pending()"
            [problem]="problemOf('areas')"
            (searchChange)="facade.setFilter('areas', { search: $event })"
            (showInactiveChange)="facade.setFilter('areas', { showInactive: $event })"
            (saveRow)="save('areas', $event)"
            (toggleActive)="toggle('areas', $event)"
          />
        </div>
      }
      <ng-container drawerFooter>
        <button type="button" class="btn ghost" (click)="closed.emit()">{{ i18n.t('platform.close') }}</button>
      </ng-container>
    </app-drawer>
  `,
  styles: `
    .columns {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 16px;
      align-items: start;
    }

    @media (max-width: 899px) {
      .columns {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationsDrawerComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(LocationsFacade);
  private readonly toast = inject(ToastService);
  private readonly columns = viewChildren(LocationColumnComponent);

  /** The governorate whose locations are edited; null closes the drawer. */
  readonly governorate = input<GovernorateListItem | null>(null);
  readonly closed = output<void>();

  protected readonly title = computed(() => {
    const governorate = this.governorate();
    return governorate === null ? '' : this.i18n.tf('platform.locations.title', { name: pick(governorate.nameEn, governorate.nameAr, this.i18n.lang()) });
  });

  protected readonly pending = computed(() => this.facade.save.pending() || this.facade.toggle.pending());

  constructor() {
    effect(() => {
      const governorate = this.governorate();
      untracked(() => (governorate === null ? this.facade.close() : this.facade.open(governorate)));
    });
  }

  protected rowsOf(level: LocationLevel): readonly LocationListItem[] {
    const source = level === 'cities' ? this.facade.cities : level === 'districts' ? this.facade.districts : this.facade.areas;
    return source.hasValue() ? source.value() : [];
  }

  /** The refusal shown in the column the last mutation addressed. */
  protected problemOf(level: LocationLevel): ProblemDetails | null {
    return this.facade.activeLevel() === level ? (this.facade.save.problem() ?? this.facade.toggle.problem()) : null;
  }

  protected async save(level: LocationLevel, input: LocationColumnSave): Promise<void> {
    const parentId = this.facade.parentOf(level);
    if (parentId === null) {
      return;
    }

    this.facade.toggle.reset();
    const result = await this.facade.save.execute({ level, parentId, id: input.id, request: input.request });
    if (result !== null) {
      this.columns().find((column) => column.level() === level)?.saved();
      this.toast.success(this.i18n.t('platform.locations.saved'));
    }
  }

  protected async toggle(level: LocationLevel, row: LocationListItem): Promise<void> {
    this.facade.save.reset();
    const result = await this.facade.toggle.execute({ level, id: row.id, active: !row.isActive });
    if (result !== null) {
      this.toast.success(this.i18n.t(row.isActive ? 'platform.locations.deactivated' : 'platform.locations.activated'));
    }
  }

  protected onOpenChange(open: boolean): void {
    if (!open) {
      this.closed.emit();
    }
  }
}

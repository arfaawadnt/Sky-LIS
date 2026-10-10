import type { PageRequest, Paged } from '../../../core/api/paging';
import type { TableColumn } from '../data-table/data-table.types';
import type { IconName } from '../icon/icon.component';

/** What the list is filtered by: the search text and the active/inactive choice (`null` = all). */
export interface MasterDataFilter {
  readonly search: string;
  readonly active: boolean | null;
}

/** The persistence the page talks to; every call returns a promise (the module's `*Api` service, `firstValueFrom`). */
export interface MasterDataAdapter<TItem, TDto> {
  list(filter: MasterDataFilter, page: PageRequest, signal?: AbortSignal): Promise<Paged<TItem>>;
  create(dto: TDto): Promise<unknown>;
  update(id: string, dto: TDto): Promise<unknown>;
  /** Deactivate / reactivate; when absent the page shows no activation actions. */
  setActive?(id: string, active: boolean): Promise<unknown>;
}

export interface SelectOption {
  readonly value: string;
  /** Already translated, or the English text with `labelAr` for Arabic. */
  readonly label: string;
  readonly labelAr?: string;
}

export type SelectOptionsSource = readonly SelectOption[] | (() => Promise<readonly SelectOption[]>);

export type FieldKind = 'text' | 'textarea' | 'number' | 'select' | 'boolean' | 'localizedText' | 'date' | 'code';

export interface FieldSchema {
  /** The form control name; for `localizedText` the DTO carries `<name>En` / `<name>Ar`. */
  readonly name: string;
  readonly kind: FieldKind;
  /** i18n key of the label; defaults to `<i18nPrefix>.field.<name>`. */
  readonly label?: string;
  /** i18n key of a hint under the control. */
  readonly hint?: string;
  readonly required?: boolean;
  readonly maxLength?: number;
  readonly min?: number;
  readonly max?: number;
  readonly step?: number;
  /** `select` only: static options or an async provider (resolved once when the page opens). */
  readonly options?: SelectOptionsSource;
  /** Cannot change once created (a code); disabled in the edit drawer. */
  readonly immutable?: boolean;
  /** Takes the full row of the two-column form grid (default for textarea and localizedText). */
  readonly full?: boolean;
  /** Default value of a new record. */
  readonly defaultValue?: unknown;
}

/** An extra per-row action (beyond edit and activate/deactivate), rendered as an icon button in the actions cell. */
export interface RowAction<TItem> {
  readonly id: string;
  /** i18n key. */
  readonly label: string;
  readonly icon: IconName;
  readonly permission?: string;
  readonly danger?: boolean;
  readonly visible?: (item: TItem) => boolean;
  readonly run: (item: TItem) => void | Promise<void>;
}

/** The permission keys gating the page's affordances (`*appCan`); the server decides regardless. */
export interface MasterDataPermissions {
  readonly create?: string;
  readonly update?: string;
  readonly activate?: string;
}

export type FormValue = Readonly<Record<string, unknown>>;

export interface MasterDataConfig<TItem, TDto> {
  /** `treasury.catalogue` → title `treasury.catalogue.title`, fields `treasury.catalogue.field.<name>`, errors `treasury.catalogue.error.<CODE>`. */
  readonly i18nPrefix: string;
  readonly columns: readonly TableColumn<TItem>[];
  readonly fields: readonly FieldSchema[];
  readonly adapter: MasterDataAdapter<TItem, TDto>;
  readonly permissions?: MasterDataPermissions;
  readonly rowActions?: readonly RowAction<TItem>[];
  /** `id` by default. */
  readonly rowId?: (item: TItem) => string;
  /** `isActive` by default. */
  readonly isActive?: (item: TItem) => boolean;
  /** Row → form value; by default each field reads `item[name]` (`localizedText` reads `<name>En`/`<name>Ar` or `{ en, ar }`). */
  readonly toForm?: (item: TItem) => FormValue;
  /** Form value → DTO; by default the value itself with `localizedText` flattened to `<name>En` / `<name>Ar`. */
  readonly toDto?: (value: FormValue, item: TItem | null) => TDto;
  /** `server` (default): the adapter receives the search text; `client`: the page filters the loaded rows by their visible text. */
  readonly searchMode?: 'server' | 'client';
  readonly pageSize?: number;
  /** Drawer width for wide forms. */
  readonly drawerSize?: 'md' | 'lg' | 'xl';
  /** Show the active/inactive filter and the activation actions (default: when the adapter has `setActive`). */
  readonly activation?: boolean;
}

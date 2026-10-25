import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { map, of, startWith, switchMap } from 'rxjs';

import {
  ANNOUNCEMENT_AUDIENCE_STATUSES,
  ANNOUNCEMENT_BODY_MAX,
  ANNOUNCEMENT_IMAGE_MAX_BYTES,
  ANNOUNCEMENT_LINK_MAX,
  ANNOUNCEMENT_PLACEMENTS,
  ANNOUNCEMENT_TITLE_MAX,
  type AnnouncementAudienceStatus,
  type AnnouncementPlacement,
  type AnnouncementRequest,
  type AnnouncementView,
} from '../../../core/api/announcement.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { blockedSave } from '../../../shared/forms/blocked-save';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { AnnouncementsFacade } from '../state/announcements.facade';
import { AnnouncementPreviewComponent } from '../ui/announcement-preview.component';
import { type AnnouncementState, announcementState, fromLocalInput, toLocalInput } from '../ui/announcement-time';
import { formatDateTime, formatNumber, pick } from '../ui/format';

/** An absolute https address (the server's rule, ANN-002). */
const HTTPS_LINK = /^https:\/\/[^\s/?#@]+(?:[/?#]\S*)?$/i;

const STATE_TONES: Readonly<Record<AnnouncementState, BadgeTone>> = { Live: 'ok', Scheduled: 'info', Ended: 'neutral', Inactive: 'neutral' };

type AnnouncementForm = ReturnType<AnnouncementsPage['buildForm']>;

/**
 * SA-22 Announcements (change request LIS-CR-013, C-17; decision D-4: National Technology's own and partner announcements, shown
 * in the laboratory sign-in hero or as the dashboard welcome card). The register lists every announcement with its state (live,
 * scheduled, ended, inactive) and counters; the editor drawer holds the texts in both languages, the link, the placement, the
 * schedule, the audience (country, plan, status, clients), the image (upload / replace / remove, once saved) and a live preview
 * in both languages. A new announcement is a draft until it is activated. `platform.manage` writes; `platform.view` reads.
 */
@Component({
  selector: 'app-platform-announcements-page',
  imports: [
    ReactiveFormsModule,
    CanDirective,
    SaveShortcutDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    FormFieldComponent,
    IconComponent,
    AnnouncementPreviewComponent,
  ],
  templateUrl: './announcements.page.html',
  styleUrls: ['./platform-pages.css', './announcements.page.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnnouncementsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(AnnouncementsFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly access = inject(PermissionsService);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  protected readonly placements = ANNOUNCEMENT_PLACEMENTS;
  protected readonly statuses = ANNOUNCEMENT_AUDIENCE_STATUSES;
  protected readonly limits = { title: ANNOUNCEMENT_TITLE_MAX, body: ANNOUNCEMENT_BODY_MAX, link: ANNOUNCEMENT_LINK_MAX };

  protected readonly canManage = computed(() => this.access.can(PLATFORM_PERMISSIONS.platformManage));

  protected readonly columns: readonly TableColumn<AnnouncementView>[] = [
    { key: 'title', header: 'platform.announcements.column.title', sortable: true, value: (row) => pick(row.titleEn, row.titleAr, this.i18n.lang()) },
    { key: 'placement', header: 'platform.announcements.column.placement', sortable: true, width: '130px' },
    { key: 'fromUtc', header: 'platform.announcements.column.from', sortable: true, width: '150px', value: (row) => formatDateTime(row.fromUtc, this.i18n.lang()) },
    { key: 'toUtc', header: 'platform.announcements.column.to', sortable: true, width: '150px', secondary: true, value: (row) => (row.toUtc === null ? '—' : formatDateTime(row.toUtc, this.i18n.lang())) },
    { key: 'audience', header: 'platform.announcements.column.audience', secondary: true, value: (row) => this.audienceText(row) },
    { key: 'state', header: 'platform.announcements.column.state', width: '120px' },
    { key: 'impressions', header: 'platform.announcements.column.impressions', sortable: true, mono: true, align: 'end', width: '110px', value: (row) => formatNumber(row.impressions, this.i18n.lang()) },
    { key: 'clicks', header: 'platform.announcements.column.clicks', sortable: true, mono: true, align: 'end', width: '90px', value: (row) => formatNumber(row.clicks, this.i18n.lang()) },
  ];

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<AnnouncementView | null>(null);
  protected readonly form = signal<AnnouncementForm | null>(null);
  protected readonly saveProblem = signal<ProblemDetails | null>(null);
  protected readonly imageError = signal<string | null>(null);
  protected readonly blockedSave = blockedSave();

  /** The editor's image through an object URL (the admin endpoint needs the bearer token); revoked when replaced or closed. */
  protected readonly imageUrl = signal<string | null>(null);

  /** The filter of the audience's client list. */
  protected readonly clientSearch = signal('');

  protected readonly visibleClients = computed(() => {
    const search = this.clientSearch().trim().toLowerCase();
    const clients = this.facade.clientOptions();
    return search === '' ? clients : clients.filter((client) => `${client.nameEn} ${client.nameAr} ${client.portalSlug}`.toLowerCase().includes(search));
  });

  /** The live values of the open form, for the preview and the client list. */
  protected readonly formValue = toSignal(
    toObservable(this.form).pipe(switchMap((form) => (form === null ? of(null) : form.valueChanges.pipe(startWith(null), map(() => form.getRawValue()))))),
    { initialValue: null },
  );

  protected readonly pickedClients = computed<ReadonlySet<string>>(() => new Set(this.formValue()?.clientIds ?? []));

  protected readonly previewPlacement = computed<AnnouncementPlacement>(() => this.formValue()?.placement ?? 'SignIn');

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    inject(DestroyRef).onDestroy(() => this.setImageUrl(null));
  }

  protected name(item: { readonly nameEn: string; readonly nameAr: string }): string {
    return pick(item.nameEn, item.nameAr, this.i18n.lang());
  }

  protected count(value: number): string {
    return formatNumber(value, this.i18n.lang());
  }

  protected state(row: AnnouncementView): AnnouncementState {
    return announcementState(row);
  }

  protected stateTone(row: AnnouncementView): BadgeTone {
    return STATE_TONES[this.state(row)];
  }

  protected placementLabel(placement: AnnouncementPlacement): string {
    return this.i18n.t(`platform.announcements.placement.${placement}`);
  }

  protected statusLabel(status: AnnouncementAudienceStatus): string {
    return this.i18n.t(`platform.announcements.audienceStatus.${status}`);
  }

  /** "Everyone", or the criteria in words: "Egypt · Starter · Trial · 3 clients". */
  protected audienceText(row: AnnouncementView): string {
    const parts: string[] = [];
    const country = this.facade.countryOptions().find((item) => item.id === row.audienceCountryId);
    if (row.audienceCountryId !== null) {
      parts.push(country ? this.name(country) : this.i18n.t('platform.announcements.audience.unknownCountry'));
    }

    const plan = this.facade.planOptions().find((item) => item.id === row.audiencePlanId);
    if (row.audiencePlanId !== null) {
      parts.push(plan ? plan.code : this.i18n.t('platform.announcements.audience.unknownPlan'));
    }

    if (row.audienceStatus !== 'Any') {
      parts.push(this.statusLabel(row.audienceStatus));
    }

    if (row.audienceClientIds.length > 0) {
      parts.push(this.i18n.tc('platform.announcements.audience.clients', row.audienceClientIds.length, { count: formatNumber(row.audienceClientIds.length, this.i18n.lang()) }));
    }

    return parts.length === 0 ? this.i18n.t('platform.announcements.audience.everyone') : parts.join(' · ');
  }

  protected onPlacementFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setPlacement(value === 'SignIn' || value === 'Welcome' ? value : null);
  }

  protected onActive(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === 'all' ? null : value === 'active');
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected onClientSearch(event: Event): void {
    this.clientSearch.set((event.target as HTMLInputElement).value);
  }

  protected toggleClient(form: AnnouncementForm, clientId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = form.controls.clientIds.value;
    form.controls.clientIds.setValue(checked ? [...current.filter((id) => id !== clientId), clientId] : current.filter((id) => id !== clientId));
    form.controls.clientIds.markAsDirty();
  }

  protected clearClients(form: AnnouncementForm): void {
    form.controls.clientIds.setValue([]);
    form.controls.clientIds.markAsDirty();
  }

  protected openEdit(announcement: AnnouncementView | null): void {
    this.editing.set(announcement);
    this.saveProblem.set(null);
    this.imageError.set(null);
    this.blockedSave.reset();
    this.clientSearch.set('');
    const form = this.buildForm(announcement);
    if (!this.canManage()) {
      form.disable();
    }

    this.form.set(form);
    this.setImageUrl(null);
    if (announcement?.imageVersion != null) {
      void this.loadImage(announcement.id);
    }

    this.drawerOpen.set(true);
  }

  protected onDrawerClosed(): void {
    this.setImageUrl(null);
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (form === null || form.disabled || this.facade.save.pending() || !this.blockedSave.check(form, '#platform-announcement-form')) {
      return;
    }

    const request = this.toRequest(form);
    if (request === null) {
      return;
    }

    this.saveProblem.set(null);
    const wasNew = this.editing() === null;
    const id = await this.facade.save.execute({ id: this.editing()?.id ?? null, request });
    if (id === null) {
      const problem = this.facade.save.problem();
      if (problem !== null) {
        problemToFormErrors(problem, form);
        this.saveProblem.set(problem);
      }

      return;
    }

    if (wasNew) {
      // A new announcement stays open so its image can be added (the image needs the saved id).
      this.editing.set(await this.facade.get(id));
      form.markAsPristine();
      this.toast.success(this.i18n.t('platform.announcements.created'));
      return;
    }

    this.drawerOpen.set(false);
    this.toast.success(this.i18n.t('platform.announcements.saved'));
  }

  protected async toggle(row: AnnouncementView): Promise<void> {
    const key = row.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t(`platform.${key}`),
      message: this.i18n.tf(`platform.announcements.confirm.${key}`, { title: pick(row.titleEn, row.titleAr, this.i18n.lang()) }),
      danger: row.isActive,
    });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.setActive.execute({ id: row.id, active: !row.isActive })) !== null) {
      this.toast.success(this.i18n.t(row.isActive ? 'platform.deactivated' : 'platform.activated'));
      return;
    }

    this.toast.error(this.problemText(this.facade.setActive.problem()));
  }

  /** Uploads the chosen image; the server decides the type by the bytes (PNG, JPEG, WebP) and the size (≤ 512 KB). */
  protected async onImageSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';   // the same file can be chosen again after a refusal
    const announcement = this.editing();
    if (file === undefined || announcement === null || this.facade.uploadImage.pending()) {
      return;
    }

    this.imageError.set(null);
    if (file.size > ANNOUNCEMENT_IMAGE_MAX_BYTES) {
      this.imageError.set(this.i18n.t('platform.announcements.error.ANN-007'));
      return;
    }

    const uploaded = await this.facade.uploadImage.execute({ id: announcement.id, file });
    if (uploaded === null) {
      this.imageError.set(this.problemText(this.facade.uploadImage.problem()));
      return;
    }

    this.editing.set({ ...announcement, imageVersion: uploaded.imageVersion });
    await this.loadImage(announcement.id);
    this.toast.success(this.i18n.t('platform.announcements.image.saved'));
  }

  protected async removeImage(): Promise<void> {
    const announcement = this.editing();
    if (announcement === null || this.facade.removeImage.pending()) {
      return;
    }

    this.imageError.set(null);
    if ((await this.facade.removeImage.execute(announcement.id)) === null) {
      this.imageError.set(this.problemText(this.facade.removeImage.problem()));
      return;
    }

    this.editing.set({ ...announcement, imageVersion: null });
    this.setImageUrl(null);
    this.toast.success(this.i18n.t('platform.announcements.image.removed'));
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'platform.announcements', problem);
  }

  /** The link caption of the preview in the preview's own language (both dictionaries hold both captions). */
  protected linkLabel(lang: 'en' | 'ar'): string {
    return this.i18n.t(lang === 'ar' ? 'platform.announcements.preview.linkAr' : 'platform.announcements.preview.linkEn');
  }

  private async loadImage(id: string): Promise<void> {
    try {
      const blob = await this.facade.image(id);
      if (this.editing()?.id === id) {
        this.setImageUrl(URL.createObjectURL(blob));
      }
    } catch {
      this.setImageUrl(null);   // a missing image shows none; the register still says it has one until reloaded
    }
  }

  private setImageUrl(url: string | null): void {
    const previous = this.imageUrl();
    if (previous !== null) {
      URL.revokeObjectURL(previous);
    }

    this.imageUrl.set(url);
  }

  private toRequest(form: AnnouncementForm): AnnouncementRequest | null {
    const value = form.getRawValue();
    const fromUtc = fromLocalInput(value.from);
    if (fromUtc === null) {
      form.controls.from.setErrors({ required: true });
      return null;
    }

    const text = (input: string): string | null => (input.trim() === '' ? null : input.trim());
    return {
      titleEn: value.titleEn.trim(),
      titleAr: value.titleAr.trim(),
      bodyEn: text(value.bodyEn),
      bodyAr: text(value.bodyAr),
      linkUrl: text(value.linkUrl),
      placement: value.placement,
      fromUtc,
      toUtc: fromLocalInput(value.to),
      audienceCountryId: value.countryId === '' ? null : value.countryId,
      audiencePlanId: value.planId === '' ? null : value.planId,
      audienceStatus: value.status,
      audienceClientIds: value.clientIds,
    };
  }

  private buildForm(announcement: AnnouncementView | null) {
    return new FormGroup({
      titleEn: new FormControl(announcement?.titleEn ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(ANNOUNCEMENT_TITLE_MAX)] }),
      titleAr: new FormControl(announcement?.titleAr ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(ANNOUNCEMENT_TITLE_MAX)] }),
      bodyEn: new FormControl(announcement?.bodyEn ?? '', { nonNullable: true, validators: [Validators.maxLength(ANNOUNCEMENT_BODY_MAX)] }),
      bodyAr: new FormControl(announcement?.bodyAr ?? '', { nonNullable: true, validators: [Validators.maxLength(ANNOUNCEMENT_BODY_MAX)] }),
      linkUrl: new FormControl(announcement?.linkUrl ?? '', { nonNullable: true, validators: [Validators.maxLength(ANNOUNCEMENT_LINK_MAX), Validators.pattern(HTTPS_LINK)] }),
      placement: new FormControl<AnnouncementPlacement>(announcement?.placement ?? 'SignIn', { nonNullable: true }),
      from: new FormControl(toLocalInput(announcement?.fromUtc ?? new Date().toISOString()), { nonNullable: true, validators: [Validators.required] }),
      to: new FormControl(toLocalInput(announcement?.toUtc), { nonNullable: true }),
      countryId: new FormControl(announcement?.audienceCountryId ?? '', { nonNullable: true }),
      planId: new FormControl(announcement?.audiencePlanId ?? '', { nonNullable: true }),
      status: new FormControl<AnnouncementAudienceStatus>(announcement?.audienceStatus ?? 'Any', { nonNullable: true }),
      clientIds: new FormControl<readonly string[]>(announcement?.audienceClientIds ?? [], { nonNullable: true }),
    });
  }
}

import { ChangeDetectionStrategy, Component, DestroyRef, type ElementRef, Injector, afterNextRender, effect, inject, input, model, output, untracked, viewChild } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';
import { focusFirst, trapTab } from '../overlay/focus-trap';
import { OverlayStackService } from '../overlay/overlay-stack.service';

export type DrawerSize = 'md' | 'lg' | 'xl';

let nextDrawerId = 0;

/**
 * `<app-drawer [(open)]="editing" [title]="…"> …form… <ng-container drawerFooter>…buttons…</ng-container></app-drawer>`:
 * the side panel for forms (SRS "editors"). Slides in from the inline end (so the start side in RTL), with a
 * scrim; Esc or the scrim close it (`dismissible`), focus moves inside and is trapped, and returns to the opener
 * on close. `closed` fires after every close, whatever triggered it.
 */
@Component({
  selector: 'app-drawer',
  imports: [IconComponent],
  templateUrl: './drawer.component.html',
  styleUrl: './drawer.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'onEscape($event)' },
})
export class DrawerComponent {
  protected readonly i18n = inject(I18nService);
  private readonly overlay = inject(OverlayStackService);
  private readonly injector = inject(Injector);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private readonly token = Symbol('drawer');
  private previouslyFocused: HTMLElement | null = null;
  private wasOpen = false;

  protected readonly titleId = `app-drawer-${++nextDrawerId}-title`;

  readonly open = model(false);
  readonly title = input('');
  readonly size = input<DrawerSize>('md');
  readonly dismissible = input(true);
  readonly closed = output<void>();

  constructor() {
    effect(() => {
      const isOpen = this.open();
      untracked(() => (isOpen ? this.onOpened() : this.onClosed()));
    });
    inject(DestroyRef).onDestroy(() => this.overlay.remove(this.token));
  }

  close(): void {
    this.open.set(false);
  }

  protected dismiss(): void {
    if (this.dismissible()) {
      this.close();
    }
  }

  protected onEscape(event: Event): void {
    if (this.open() && this.dismissible() && this.overlay.isTop(this.token)) {
      event.preventDefault();
      this.close();
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    const panel = this.panel()?.nativeElement;
    if (panel) {
      trapTab(event, panel);
    }
  }

  private onOpened(): void {
    this.wasOpen = true;
    this.previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.overlay.push(this.token);
    afterNextRender(
      () => {
        const panel = this.panel()?.nativeElement;
        if (panel && !panel.contains(document.activeElement)) {
          focusFirst(panel, panel.querySelector<HTMLElement>('.body'));
        }
      },
      { injector: this.injector },
    );
  }

  private onClosed(): void {
    if (!this.wasOpen) {
      return;
    }

    this.wasOpen = false;
    this.overlay.remove(this.token);
    const opener = this.previouslyFocused;
    this.previouslyFocused = null;
    if (opener?.isConnected) {
      opener.focus();
    }

    this.closed.emit();
  }
}

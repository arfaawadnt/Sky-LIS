import { ChangeDetectionStrategy, Component, DestroyRef, type ElementRef, Injector, afterNextRender, effect, inject, input, model, output, untracked, viewChild } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';
import { focusFirst, trapTab } from '../overlay/focus-trap';
import { OverlayStackService } from '../overlay/overlay-stack.service';

export type DialogSize = 'sm' | 'md' | 'lg';

let nextDialogId = 0;

/**
 * `<app-dialog [(open)]="show" [title]="…" size="sm"> …body… <ng-container dialogFooter>…</ng-container></app-dialog>`:
 * a centred modal (prototype `.modal`). Same overlay rules as the drawer: scrim, Esc on the topmost overlay,
 * focus moved inside and trapped, restored on close. `ConfirmService` builds confirm/prompt dialogs on top of it.
 */
@Component({
  selector: 'app-dialog',
  imports: [IconComponent],
  templateUrl: './dialog.component.html',
  styleUrl: './dialog.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'onEscape($event)' },
})
export class DialogComponent {
  protected readonly i18n = inject(I18nService);
  private readonly overlay = inject(OverlayStackService);
  private readonly injector = inject(Injector);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private readonly token = Symbol('dialog');
  private previouslyFocused: HTMLElement | null = null;
  private wasOpen = false;

  protected readonly titleId = `app-dialog-${++nextDialogId}-title`;

  readonly open = model(false);
  readonly title = input('');
  readonly size = input<DialogSize>('md');
  readonly danger = input(false);
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

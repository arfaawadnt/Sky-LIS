import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';

import { PermissionsService } from '../../core/permissions/permissions.service';

/**
 * `<button *appCan="'treasury.approve'">` renders its host only while the signed-in user holds the permission.
 * Affordance only (pitfall P-20): the server decides on every request; this keeps the screen honest about what
 * the user can do. Reacts to session changes because `can()` reads the auth signals.
 */
@Directive({ selector: '[appCan]' })
export class CanDirective {
  private readonly permissions = inject(PermissionsService);
  private readonly view = inject(ViewContainerRef);
  private readonly template = inject(TemplateRef<unknown>);

  readonly appCan = input.required<string>();

  constructor() {
    effect(() => {
      const allowed = this.permissions.can(this.appCan());
      this.view.clear();
      if (allowed) {
        this.view.createEmbeddedView(this.template);
      }
    });
  }
}

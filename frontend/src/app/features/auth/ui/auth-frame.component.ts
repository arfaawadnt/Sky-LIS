import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

/**
 * The ground of the sign-in pages, design 5c "light premium" (2026-10-20): the Sky LIS logo large in the header with
 * the language switch, the hero on the start side, the white card (the page's content) on the end side, and the footer
 * links. Shared by the laboratory and Super Admin sign-in and the find page (LIS-CR-005). The texts arrive translated.
 */
@Component({
  selector: 'app-auth-frame',
  imports: [RouterLink, IconComponent],
  templateUrl: './auth-frame.component.html',
  styleUrl: './auth-frame.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthFrameComponent {
  protected readonly i18n = inject(I18nService);

  readonly eyebrow = input.required<string>();
  readonly heroTitle = input.required<string>();
  readonly lead = input.required<string>();
  /** The id of the card's heading. */
  readonly labelledBy = input.required<string>();
  /** "New laboratory? Subscribe now" in the footer; off on the Super Admin sign-in. */
  readonly showSubscribe = input(true);

  protected readonly otherLanguage = computed(() => (this.i18n.lang() === 'en' ? 'العربية' : 'English'));

  protected toggleLanguage(): void {
    this.i18n.setLanguage(this.i18n.lang() === 'en' ? 'ar' : 'en');
  }
}

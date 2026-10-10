import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { App } from './app';
import { I18nService } from './core/i18n/i18n.service';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders the brand and reflects the language direction on the host', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.getAttribute('dir')).toBe('ltr');
    expect(host.querySelector('.brand')).not.toBeNull();

    TestBed.inject(I18nService).setLanguage('ar');
    await fixture.whenStable();
    expect(host.getAttribute('dir')).toBe('rtl');
    expect(document.documentElement.dir).toBe('rtl');
  });
});

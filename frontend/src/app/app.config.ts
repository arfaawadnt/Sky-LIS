import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';

import { routes } from './app.routes';
import { AuthStore } from './core/auth/auth.store';
import { provideSessionSync } from './core/auth/session-channel';
import { authInterceptor } from './core/http/auth.interceptor';
import { credentialEnvelopeInterceptor } from './core/http/credential-envelope.interceptor';
import { csrfInterceptor } from './core/http/csrf.interceptor';
import { problemDetailsInterceptor } from './core/http/problem-details.interceptor';
import { profileChangeInterceptor } from './core/http/profile-change.interceptor';

/**
 * Bootstrap of 01 §6.1 (D-01, D-03): zoneless, signals, fetch-based HttpClient with the five interceptors in
 * request order, and one silent refresh at start-up that restores the session from the httpOnly cookie. The
 * access token never touches web storage; `hydrate()` always resolves (failure means signed out).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding(), withViewTransitions()),
    provideHttpClient(
      withFetch(),
      withInterceptors([
        csrfInterceptor, // X-Requested-With: Platform on every API request (02 §2.3)
        authInterceptor, // bearer from memory; single-flight refresh on 401 (D-01)
        credentialEnvelopeInterceptor, // seals [CredentialField] properties before send (D-03)
        problemDetailsInterceptor, // typed application/problem+json errors
        profileChangeInterceptor, // a successful own-profile change tells the other tabs (R2-01)
      ]),
    ),
    provideSessionSync(), // tabs tell each other of a sign-in, a sign-out (C-01) or a profile change (R2-01); BroadcastChannel, no web storage
    provideAppInitializer(() => inject(AuthStore).hydrate()),
  ],
};

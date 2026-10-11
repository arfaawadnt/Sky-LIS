import type { Provider } from '@angular/core';

import { SESSION_CHANNEL, type SessionChannel, type SessionMessage } from './session-channel';
import { BROWSER_NAVIGATION, type BrowserNavigation } from './session-navigation';

/** Spec double of the address bar: records the full page loads instead of performing them. */
export class FakeBrowserNavigation implements BrowserNavigation {
  readonly loads: string[] = [];
  url = '/';

  currentUrl(): string {
    return this.url;
  }

  load(url: string): void {
    this.loads.push(url);
  }
}

/** Spec double of the tab channel: records what this tab posts; `deliver` plays a message from another tab. */
export class FakeSessionChannel implements SessionChannel {
  readonly posted: SessionMessage[] = [];
  private readonly handlers = new Set<(message: SessionMessage) => void>();

  post(message: SessionMessage): void {
    this.posted.push(message);
  }

  listen(handler: (message: SessionMessage) => void): () => void {
    this.handlers.add(handler);
    return () => this.handlers.delete(handler);
  }

  deliver(message: SessionMessage): void {
    for (const handler of this.handlers) {
      handler(message);
    }
  }
}

/** Providers for specs that exercise the end of a session. */
export function provideSessionTesting(navigation: FakeBrowserNavigation, channel = new FakeSessionChannel()): Provider[] {
  return [
    { provide: BROWSER_NAVIGATION, useValue: navigation },
    { provide: SESSION_CHANNEL, useValue: channel },
  ];
}

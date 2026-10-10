import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ConfirmService } from './confirm.service';
import { DialogHostComponent } from './dialog-host.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('ConfirmService + DialogHostComponent (shared/ui)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DialogHostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
  });

  it('resolves a confirmation with true on Confirm and false on Escape', async () => {
    const fixture = TestBed.createComponent(DialogHostComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    const host = fixture.nativeElement as HTMLElement;
    const confirm = TestBed.inject(ConfirmService);

    const accepted = confirm.confirm({ title: 'Approve', message: 'Approve request?' });
    await fixture.whenStable();
    expect(host.querySelector('[role="dialog"] h2')?.textContent?.trim()).toBe('Approve');
    const confirmButton = host.querySelector<HTMLButtonElement>('.foot .btn.primary');
    expect(document.activeElement).toBe(confirmButton);
    confirmButton?.click();
    expect(await accepted).toBe(true);

    const dismissed = confirm.confirm({ title: 'Delete', message: 'Sure?', danger: true });
    await fixture.whenStable();
    expect(host.querySelector('.foot .btn.danger')).not.toBeNull();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(await dismissed).toBe(false);
    await fixture.whenStable();
    expect(host.querySelector('[role="dialog"]')).toBeNull();

    host.remove();
  });

  it('requires a reason in promptReason and resolves to the trimmed text, or null when cancelled', async () => {
    const fixture = TestBed.createComponent(DialogHostComponent);
    const host = fixture.nativeElement as HTMLElement;
    const confirm = TestBed.inject(ConfirmService);

    const reason = confirm.promptReason({ title: 'Reject', message: 'Why?' });
    await fixture.whenStable();
    const accept = (): void => host.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();

    accept();
    await fixture.whenStable();
    expect(host.querySelector('.reason-field .error')).not.toBeNull();
    expect(confirm.current()).not.toBeNull();

    const textarea = host.querySelector<HTMLTextAreaElement>('textarea');
    if (!textarea) {
      throw new Error('reason field missing');
    }

    textarea.value = '  wrong device  ';
    textarea.dispatchEvent(new Event('input'));
    accept();
    expect(await reason).toBe('wrong device');

    const cancelled = confirm.promptReason({ title: 'Revoke', message: 'Why?' });
    await fixture.whenStable();
    host.querySelector<HTMLButtonElement>('.foot .btn:not(.primary)')?.click();
    expect(await cancelled).toBeNull();
    await settle();
  });

  it('accepts an empty reason when it is optional', async () => {
    const fixture = TestBed.createComponent(DialogHostComponent);
    const host = fixture.nativeElement as HTMLElement;
    const confirm = TestBed.inject(ConfirmService);

    const reason = confirm.promptReason({ title: 'Cancel', message: 'Optional reason', required: false });
    await fixture.whenStable();
    host.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    expect(await reason).toBe('');
  });
});

import { Directive, ElementRef, inject } from '@angular/core';
import { NgControl } from '@angular/forms';

/**
 * `<input appUppercase formControlName="code">`: normalises what is typed to upper case and trims the edges, so
 * codes are stored the way the domain compares them. Works with or without a reactive control.
 */
@Directive({ selector: 'input[appUppercase]', host: { '(input)': 'onInput()', '(blur)': 'onBlur()' } })
export class UppercaseDirective {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly ngControl = inject(NgControl, { optional: true, self: true });

  protected onInput(): void {
    this.apply(this.element.value.toUpperCase());
  }

  protected onBlur(): void {
    this.apply(this.element.value.toUpperCase().trim());
  }

  private apply(next: string): void {
    if (next === this.element.value && this.ngControl?.control?.value === next) {
      return;
    }

    const position = this.element.selectionStart;
    if (this.ngControl?.control) {
      this.ngControl.control.setValue(next);
    } else {
      this.element.value = next;
    }

    if (position !== null && this.element.selectionStart !== position) {
      this.element.setSelectionRange(position, position);
    }
  }
}

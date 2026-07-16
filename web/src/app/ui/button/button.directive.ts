import { Directive, HostBinding, Input } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

@Directive({
  selector: 'button[pmButton], a[pmButton]',
})
export class PmButton {
  @Input() pmButton: ButtonVariant = 'secondary';

  @HostBinding('class.pm-button') protected readonly baseClass = true;
  @HostBinding('class.pm-button--primary') protected get primary(): boolean {
    return this.pmButton === 'primary';
  }
  @HostBinding('class.pm-button--secondary') protected get secondary(): boolean {
    return this.pmButton === 'secondary';
  }
  @HostBinding('class.pm-button--ghost') protected get ghost(): boolean {
    return this.pmButton === 'ghost';
  }
  @HostBinding('class.pm-button--danger') protected get danger(): boolean {
    return this.pmButton === 'danger';
  }
}

import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssClose, cssSearch } from '@ng-icons/css.gg';

export interface TopBarSearchOption {
  id: string;
  primary: string;
  secondary?: string;
  leading?: string;
  snippet?: string;
}

@Component({
  selector: 'pm-top-bar-search',
  imports: [NgIcon],
  providers: [provideIcons({ cssClose, cssSearch })],
  templateUrl: './top-bar-search.html',
  styleUrl: './top-bar-search.css',
})
export class TopBarSearch {
  private static nextId = 1;

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly inputElement = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  readonly query = model('');
  readonly options = input<readonly TopBarSearchOption[]>([]);
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly ariaLabel = input.required<string>();
  readonly listboxLabel = input.required<string>();
  readonly placeholder = input.required<string>();
  readonly emptyMessage = input.required<string>();
  readonly openForQuery = input(true);
  readonly queryEdited = output<string>();
  readonly optionSelected = output<TopBarSearchOption>();

  protected readonly focused = signal(false);
  protected readonly mobileExpanded = signal(false);
  protected readonly activeIndex = signal(0);
  protected readonly listboxId = `top-bar-search-${TopBarSearch.nextId++}`;
  protected readonly popupOpen = computed(
    () =>
      this.focused() &&
      (this.options().length > 0 ||
        this.loading() ||
        !!this.error() ||
        (this.openForQuery() && !!this.query().trim())),
  );

  constructor() {
    effect(() => {
      if (this.activeIndex() >= this.options().length) this.activeIndex.set(0);
    });
  }

  expandMobile(): void {
    this.mobileExpanded.set(true);
    setTimeout(() => this.inputElement()?.nativeElement.focus());
  }

  close(): void {
    this.focused.set(false);
    this.mobileExpanded.set(false);
    this.activeIndex.set(0);
  }

  caret(): number {
    return this.inputElement()?.nativeElement.selectionStart ?? this.query().length;
  }

  focusAt(caret: number): void {
    const element = this.inputElement()?.nativeElement;
    element?.focus();
    element?.setSelectionRange(caret, caret);
  }

  protected onFocus(): void {
    this.focused.set(true);
  }

  protected onBlur(): void {
    setTimeout(() => {
      if (!this.host.nativeElement.contains(document.activeElement)) this.close();
    });
  }

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.activeIndex.set(0);
    this.queryEdited.emit(this.query());
  }

  protected onKeydown(event: KeyboardEvent): void {
    const options = this.options();
    if (event.key === 'ArrowDown' && options.length) {
      event.preventDefault();
      this.activeIndex.set((this.activeIndex() + 1) % options.length);
    } else if (event.key === 'ArrowUp' && options.length) {
      event.preventDefault();
      this.activeIndex.set((this.activeIndex() - 1 + options.length) % options.length);
    } else if (event.key === 'Enter' && options[this.activeIndex()]) {
      event.preventDefault();
      this.optionSelected.emit(options[this.activeIndex()]!);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
      this.inputElement()?.nativeElement.blur();
    }
  }

  protected select(option: TopBarSearchOption): void {
    this.optionSelected.emit(option);
  }

  protected optionId(index: number): string {
    return `${this.listboxId}-option-${index}`;
  }
}

import {
  AfterViewInit,
  Component,
  effect,
  ElementRef,
  input,
  model,
  OnDestroy,
  output,
  signal,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';
import EasyMDE from 'easymde';

import { MarkdownService } from './markdown.service';

@Component({
  selector: 'pm-markdown-editor',
  template:
    '<textarea #textarea [disabled]="disabled()" [attr.aria-label]="label()" (input)="fallbackInput($event)" (blur)="touch.emit()"></textarea>',
  styleUrl: './markdown-editor.css',
  encapsulation: ViewEncapsulation.None,
})
export class MarkdownEditor implements FormValueControl<string>, AfterViewInit, OnDestroy {
  readonly value = model.required<string>();
  readonly disabled = input(false);
  readonly label = input('Markdown description');
  readonly touch = output<void>();
  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea');
  private readonly editor = signal<EasyMDE | null>(null);
  private syncing = false;

  constructor(private readonly renderer: MarkdownService) {
    effect(() => {
      const next = this.value();
      const editor = this.editor();
      if (editor && editor.value() !== next) {
        this.syncing = true;
        editor.value(next);
        this.syncing = false;
      } else if (!editor && this.textarea() && this.textarea()!.nativeElement.value !== next) {
        this.textarea()!.nativeElement.value = next;
      }
    });
    effect(() => {
      const editor = this.editor();
      if (editor) this.updateDisabled(editor);
    });
  }

  ngAfterViewInit(): void {
    try {
      const editor = new EasyMDE({
        element: this.textarea()!.nativeElement,
        initialValue: this.value(),
        autofocus: false,
        spellChecker: false,
        status: false,
        minHeight: '180px',
        previewRender: (markdown) => this.renderer.render(markdown),
      });
      this.editor.set(editor);
      editor.codemirror.on('change', () => {
        if (!this.syncing) this.value.set(this.editor()?.value() ?? '');
      });
      editor.codemirror.on('blur', () => this.touch.emit());
      editor.codemirror.getInputField().setAttribute('aria-label', this.label());
      const container = editor.codemirror.getWrapperElement().closest('.EasyMDEContainer');
      container?.setAttribute('aria-label', this.label());
      container
        ?.querySelectorAll<HTMLButtonElement>('.editor-toolbar button')
        .forEach((button) => (button.tabIndex = 0));
      this.updateDisabled(editor);
      const scroll = editor.codemirror.getScrollerElement();
      scroll.tabIndex = 0;
      scroll.addEventListener('focus', () => editor.codemirror.focus());
    } catch {
      this.editor.set(null);
      this.textarea()!.nativeElement.value = this.value();
    }
  }

  ngOnDestroy(): void {
    this.editor()?.toTextArea();
    this.editor.set(null);
  }

  focus(options?: FocusOptions): void {
    if (this.editor()) this.editor()!.codemirror.focus();
    else this.textarea()?.nativeElement.focus(options);
  }

  protected fallbackInput(event: Event): void {
    if (!this.editor()) this.value.set((event.target as HTMLTextAreaElement).value);
  }

  private updateDisabled(editor: EasyMDE): void {
    const disabled = this.disabled();
    editor.codemirror.setOption('readOnly', disabled ? 'nocursor' : false);
    editor.codemirror
      .getWrapperElement()
      .closest('.EasyMDEContainer')
      ?.querySelectorAll<HTMLButtonElement>('.editor-toolbar button')
      .forEach((button) => (button.disabled = disabled));
  }
}

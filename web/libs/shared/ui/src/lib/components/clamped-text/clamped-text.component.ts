import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  afterRenderEffect,
  booleanAttribute,
  computed,
  inject,
  input,
  signal,
  viewChild
} from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';

let nextId = 0;

/**
 * A user's text (a name, a remark, a contact) cut after a few lines, with a real "Show more" / "Show less" button when
 * there is more to read, so a long value is readable by keyboard and by touch and not only under a mouse pointer (L00).
 * Where the full value is one click away anyway (a row that links to its detail page) set `expandable` to false.
 */
@Component({
  selector: 'app-clamped-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './clamped-text.component.html',
  styleUrl: './clamped-text.component.scss'
})
export class UiClampedTextComponent {
  private readonly uiText = inject(UI_TEXT);
  private readonly destroyRef = inject(DestroyRef);

  readonly $text = input.required<string>({ alias: 'text' });
  readonly $lines = input(2, { alias: 'lines' });
  readonly $expandable = input(true, { alias: 'expandable', transform: booleanAttribute });

  readonly textId = `ui-clamped-text-${nextId++}`;
  private readonly $body = viewChild.required<ElementRef<HTMLElement>>('body');
  readonly $expanded = signal(false);
  /** The text needs more lines than it is allowed while folded. Stays true once expanded, so the button can fold it again. */
  private readonly $overflowing = signal(false);

  readonly $showButton = computed(() => this.$expandable() && this.$overflowing());
  readonly $label = computed(() =>
    this.$expanded() ? this.uiText().showLess : this.uiText().showMore
  );

  constructor() {
    // jsdom has no layout (scrollHeight is 0), so there the text never overflows and no button shows.
    afterRenderEffect(() => {
      this.$text();
      this.$lines();
      this.measure();
    });
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const observer = new ResizeObserver(() => this.measure());
      observer.observe(this.$body().nativeElement);
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  toggle(): void {
    this.$expanded.update(expanded => !expanded);
  }

  private measure(): void {
    if (this.$expanded()) {
      return;
    }
    const body = this.$body().nativeElement;
    this.$overflowing.set(body.scrollHeight > body.clientHeight + 1);
  }
}

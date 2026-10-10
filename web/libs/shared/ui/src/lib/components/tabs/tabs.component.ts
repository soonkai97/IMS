import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  model
} from '@angular/core';

export interface TabItem {
  readonly id: string;
  readonly label: string;
  readonly badge?: number;
}

const ARROW_STEPS: Readonly<Record<string, number>> = { ArrowRight: 1, ArrowLeft: -1 };

/** Tab strip (role=tablist). The consumer renders the panel for `active` and links it with aria-labelledby. */
@Component({
  selector: 'app-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tabs.component.html',
  styleUrl: './tabs.component.scss'
})
export class UiTabsComponent {
  private readonly elementRef = inject<ElementRef<HTMLElement>>(ElementRef);

  $tabs = input.required<readonly TabItem[]>({ alias: 'tabs' });
  $label = input.required<string>({ alias: 'label' });
  $active = model.required<string>({ alias: 'active' });

  onSelect(id: string): void {
    this.$active.set(id);
  }

  /** Arrow keys move between tabs (roving tabindex pattern). */
  onKeydown(event: KeyboardEvent, index: number): void {
    const tabs = this.$tabs();
    const step = ARROW_STEPS[event.key];
    if (step === undefined) {
      return;
    }
    event.preventDefault();
    const next = (index + step + tabs.length) % tabs.length;
    this.$active.set(tabs[next].id);
    this.elementRef.nativeElement.querySelectorAll<HTMLElement>('[role="tab"]')[next]?.focus();
  }
}

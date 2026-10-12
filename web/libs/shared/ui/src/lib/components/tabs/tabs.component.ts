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
const FIRST_TAB_KEY = 'Home';
const LAST_TAB_KEY = 'End';

/** The id of a tab button, for the `aria-labelledby` of the panel it controls. */
export function tabButtonId(tabId: string): string {
  return `tab-${tabId}`;
}

/** Tab strip (role=tablist). The panel it controls is marked with `appTabPanel` and the id passed as `panel` (WCAG 4.1.2, ER-18). */
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
  $panel = input.required<string>({ alias: 'panel' });
  $active = model.required<string>({ alias: 'active' });

  readonly buttonId = tabButtonId;

  onSelect(id: string): void {
    this.$active.set(id);
  }

  /** Arrow keys move between tabs, Home and End jump to the first and last (roving tabindex pattern). */
  onKeydown(event: KeyboardEvent, index: number): void {
    const tabs = this.$tabs();
    const next = this.targetIndex(event.key, index, tabs.length);
    if (next === undefined) {
      return;
    }
    event.preventDefault();
    this.$active.set(tabs[next].id);
    this.elementRef.nativeElement.querySelectorAll<HTMLElement>('[role="tab"]')[next]?.focus();
  }

  private targetIndex(key: string, index: number, count: number): number | undefined {
    if (key === FIRST_TAB_KEY) {
      return 0;
    }
    if (key === LAST_TAB_KEY) {
      return count - 1;
    }
    const step = ARROW_STEPS[key];
    return step === undefined ? undefined : (index + step + count) % count;
  }
}

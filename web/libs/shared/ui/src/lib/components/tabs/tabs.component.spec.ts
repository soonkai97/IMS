import { Component, signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiTabPanelDirective } from './tab-panel.directive';
import { UiTabsComponent } from './tabs.component';

@Component({
  imports: [UiTabsComponent, UiTabPanelDirective],
  template: `
    <app-tabs label="Scope" panel="list-panel" [tabs]="tabs" [(active)]="$active" />
    <div appTabPanel="list-panel" [labelledByTab]="$active()">List</div>
  `
})
class HostComponent {
  tabs = [
    { id: 'mine', label: 'Mine' },
    { id: 'all', label: 'All' },
    { id: 'old', label: 'Old', badge: 3 }
  ];
  $active = signal('mine');
}

describe('UiTabsComponent and UiTabPanelDirective accessibility (ER-18)', () => {
  let spectator: Spectator<HostComponent>;
  const createComponent = createComponentFactory({ component: HostComponent });

  beforeEach(() => {
    spectator = createComponent();
  });

  const tabs = (): HTMLElement[] => spectator.queryAll<HTMLElement>('[role="tab"]');
  const press = (index: number, key: string): void => {
    tabs()[index].dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    spectator.detectChanges();
  };

  const attr = (name: string): (string | null)[] => tabs().map(tab => tab.getAttribute(name));
  const selected = (): number => attr('aria-selected').indexOf('true');

  it('is a tablist whose tabs each name the panel they control, and the panel names its tab', () => {
    expect(spectator.query('[role="tablist"]')?.getAttribute('aria-label')).toBe('Scope');
    expect(attr('aria-controls')).toEqual(['list-panel', 'list-panel', 'list-panel']);
    const panel = spectator.query('#list-panel');
    expect(panel?.getAttribute('role')).toBe('tabpanel');
    expect(panel?.getAttribute('aria-labelledby')).toBe(tabs()[0].id);
  });

  it('only the selected tab is in the tab order, and the panel follows the selection', () => {
    expect(selected()).toBe(0);
    expect(attr('tabindex')).toEqual(['0', '-1', '-1']);

    spectator.click(tabs()[1]);

    expect(selected()).toBe(1);
    expect(spectator.query('#list-panel')?.getAttribute('aria-labelledby')).toBe(tabs()[1].id);
  });

  it('arrow keys wrap around, Home and End jump to the first and last tab', () => {
    press(0, 'ArrowLeft');
    expect(selected()).toBe(2);
    expect(document.activeElement).toBe(tabs()[2]);

    press(2, 'ArrowRight');
    expect(selected()).toBe(0);

    press(0, 'End');
    expect(selected()).toBe(2);

    press(2, 'Home');
    expect(selected()).toBe(0);
  });

  it('other keys do nothing', () => {
    press(0, 'a');

    expect(selected()).toBe(0);
  });
});

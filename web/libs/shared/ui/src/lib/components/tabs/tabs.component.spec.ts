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

  it('is a tablist whose tabs each name the panel they control, and the panel names its tab', () => {
    expect(spectator.query('[role="tablist"]')?.getAttribute('aria-label')).toBe('Scope');
    expect(tabs().map(tab => tab.getAttribute('aria-controls'))).toEqual([
      'list-panel',
      'list-panel',
      'list-panel'
    ]);
    const panel = spectator.query('#list-panel');
    expect(panel?.getAttribute('role')).toBe('tabpanel');
    expect(panel?.getAttribute('aria-labelledby')).toBe(tabs()[0].id);
  });

  it('only the selected tab is in the tab order, and the panel follows the selection', () => {
    expect(tabs().map(tab => tab.getAttribute('aria-selected'))).toEqual([
      'true',
      'false',
      'false'
    ]);
    expect(tabs().map(tab => tab.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);

    spectator.click(tabs()[1]);

    expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
    expect(spectator.query('#list-panel')?.getAttribute('aria-labelledby')).toBe(tabs()[1].id);
  });

  it('arrow keys wrap around, Home and End jump to the first and last tab', () => {
    press(0, 'ArrowLeft');
    expect(tabs()[2].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs()[2]);

    press(2, 'ArrowRight');
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');

    press(0, 'End');
    expect(tabs()[2].getAttribute('aria-selected')).toBe('true');

    press(2, 'Home');
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
  });

  it('other keys do nothing', () => {
    press(0, 'a');

    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
  });
});

import { signal } from '@angular/core';
import { createDirectiveFactory, SpectatorDirective } from '@ngneat/spectator/jest';
import { ModulesStore } from '../services/modules.store';
import { ModuleDirective } from './module.directive';

describe('ModuleDirective', () => {
  const on = signal<readonly string[]>([]);
  let spectator: SpectatorDirective<ModuleDirective>;
  const createDirective = createDirectiveFactory({
    directive: ModuleDirective,
    providers: [
      { provide: ModulesStore, useValue: { isEnabled: (code: string) => on().includes(code) } }
    ]
  });

  beforeEach(() => on.set([]));

  it('renders nothing while the module is off and leaves no wrapper behind', () => {
    spectator = createDirective(`<div class="host"><p *appModule="'batch'">Lot</p></div>`);

    expect(spectator.query('.host')?.children).toHaveLength(0);
  });

  it('renders while the module is on, and follows a switch in both directions', () => {
    on.set(['batch']);
    spectator = createDirective(`<p *appModule="'batch'" class="lot">Lot</p>`);
    expect(spectator.query('.lot')).not.toBeNull();

    on.set([]);
    spectator.detectChanges();
    expect(spectator.query('.lot')).toBeNull();

    on.set(['batch']);
    spectator.detectChanges();
    expect(spectator.queryAll('.lot')).toHaveLength(1);
  });

  it('asks about its own module only', () => {
    on.set(['accounting']);
    spectator = createDirective(`<p *appModule="'batch'" class="lot">Lot</p>`);

    expect(spectator.query('.lot')).toBeNull();
  });
});

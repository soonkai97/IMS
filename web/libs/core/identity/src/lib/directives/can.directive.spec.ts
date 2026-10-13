import { signal } from '@angular/core';
import { createDirectiveFactory, SpectatorDirective } from '@ngneat/spectator/jest';
import { UserInfo } from '@ims/api';
import { PERMISSION } from '@ims/models';
import { AuthService } from '../services/auth.service';
import { CanDirective } from './can.directive';

function userWith(permissions: string[]): UserInfo {
  return {
    id: '00000000-0000-4000-8000-000000000001',
    username: 'u',
    displayName: 'U',
    role: 'Clerk',
    permissions,
    warehouseIds: []
  };
}

describe('CanDirective', () => {
  const user = signal<UserInfo | undefined>(undefined);
  let spectator: SpectatorDirective<CanDirective>;
  const createDirective = createDirectiveFactory({
    directive: CanDirective,
    providers: [{ provide: AuthService, useValue: { $user: user.asReadonly() } }]
  });

  beforeEach(() => user.set(undefined));

  it('renders nothing while signed out', () => {
    spectator = createDirective(`<p *appCan="'cost.view'" class="cost">1.00</p>`);

    expect(spectator.query('.cost')).toBeNull();
  });

  it('renders nothing without the permission and leaves no wrapper behind', () => {
    user.set(userWith([PERMISSION.StockRead]));
    spectator = createDirective(`<div class="host"><p *appCan="'cost.view'">1.00</p></div>`);

    expect(spectator.query('.host')?.children).toHaveLength(0);
  });

  it('renders with the permission and follows a change of the signed-in user', () => {
    user.set(userWith([PERMISSION.CostView]));
    spectator = createDirective(`<p *appCan="'cost.view'" class="cost">1.00</p>`);
    expect(spectator.query('.cost')).not.toBeNull();

    user.set(userWith([]));
    spectator.detectChanges();
    expect(spectator.query('.cost')).toBeNull();

    user.set(userWith([PERMISSION.CostView]));
    spectator.detectChanges();
    expect(spectator.queryAll('.cost')).toHaveLength(1);
  });

  it('accepts a list and shows when any one is held', () => {
    user.set(userWith([PERMISSION.ValuationView]));
    spectator = createDirective(`<p *appCan="['cost.view', 'valuation.view']" class="any">x</p>`);

    expect(spectator.query('.any')).not.toBeNull();
  });

  it('keeps cost and valuation apart: cost.view does not unlock a value', () => {
    user.set(userWith([PERMISSION.CostView]));
    spectator = createDirective(`<p *appCan="'valuation.view'" class="value">9.00</p>`);

    expect(spectator.query('.value')).toBeNull();
  });
});

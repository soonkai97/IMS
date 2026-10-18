import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { NEVER, of, throwError } from 'rxjs';
import { PermissionDto, PermissionGroup, PermissionsClient, RoleDto, RolesClient } from '@ims/api';
import { provideUiText } from '@ims/core/i18n';
import { API_ERROR_KIND, ApiError } from '@ims/models';
import { RoleEditPageComponent } from './role-edit.component';

const ID = '00000000-0000-4000-8000-000000000011';
const OTHER = '00000000-0000-4000-8000-000000000012';

const permission = (
  code: string,
  group: PermissionGroup,
  extra: Partial<PermissionDto> = {}
): PermissionDto => ({
  code,
  group,
  description: `${code} help`,
  isDangerous: false,
  adminCore: false,
  requires: [],
  ...extra
});
const CATALOG: PermissionDto[] = [
  permission('users.read', PermissionGroup.Administration, { adminCore: true }),
  permission('roles.manage', PermissionGroup.Administration, {
    adminCore: true,
    isDangerous: true,
    requires: ['users.read']
  }),
  permission('stock.read', PermissionGroup.StockAndReports),
  permission('document.post', PermissionGroup.Documents)
];
const role = (extra: Partial<RoleDto> = {}): RoleDto => ({
  id: ID,
  code: 'night',
  name: 'Night clerk',
  description: 'Works at night',
  isSystem: false,
  isActive: true,
  permissions: ['stock.read'],
  userCount: 2,
  concurrencyToken: 'v1',
  ...extra
});
const problem = (status: number, kind: ApiError['kind']): ApiError => ({
  isApiError: true,
  kind,
  status,
  title: '',
  detail: 'x',
  fieldErrors: {},
  extensions: {}
});

describe('RoleEditPageComponent (RP02: a role and what it allows, view only)', () => {
  let spectator: Spectator<RoleEditPageComponent>;
  const client = { get: jest.fn() };
  const catalogList = jest.fn();
  const createComponent = createComponentFactory({
    component: RoleEditPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      provideUiText(),
      { provide: RolesClient, useValue: client },
      { provide: PermissionsClient, useValue: { list: catalogList } }
    ]
  });

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      spectator.detectChanges();
      await spectator.fixture.whenStable();
    }
  }

  async function open(id: string | undefined = ID): Promise<void> {
    spectator = createComponent({ props: { id } as never });
    await settle();
  }

  const row = (code: string) => spectator.query(`[data-permission="${code}"]`) as HTMLElement;
  const box = (code: string) => row(code).querySelector('input') as HTMLInputElement;
  const text = () => spectator.element.textContent ?? '';

  beforeEach(() => {
    jest.resetAllMocks();
    catalogList.mockReturnValue(of(CATALOG));
    client.get.mockReturnValue(of(role()));
  });

  it('lists the permissions by section, one fieldset and legend each, ticked as the role holds them', async () => {
    await open();

    expect(spectator.query('h1')?.textContent).toContain('Night clerk');
    const legends = spectator.queryAll('fieldset > legend h2').map(h => h.textContent?.trim());
    expect(legends).toEqual(['Administration', 'Stock and reports', 'Documents']);
    expect(box('stock.read').checked).toBe(true);
    expect(box('document.post').checked).toBe(false);
    expect(row('stock.read').textContent).toContain('stock.read help');
    expect(row('roles.manage').textContent).toContain('High impact');
    expect(row('roles.manage').textContent).toContain('Needs: See users');
    expect(text()).toContain('1 of 1 on');
  });

  it('shows the text, the state and the number of people', async () => {
    await open();

    expect(text()).toContain('Works at night');
    expect(text()).toContain('2 people have this role.');
    expect(text()).toContain('Active');
    expect(text()).toContain('View only');
    expect(text()).toContain('Custom');
  });

  it('is view-only: every box is disabled and there is no button to save or delete', async () => {
    await open();

    const boxes = spectator.queryAll<HTMLInputElement>('fieldset input');
    expect(boxes.length).toBe(CATALOG.length);
    expect(boxes.every(input => input.disabled)).toBe(true);
    expect(spectator.queryAll('button').length).toBe(0);
  });

  it('says in words that the locked role cannot be changed, and says nothing of the kind for another', async () => {
    client.get.mockReturnValue(
      of(role({ isSystem: true, permissions: ['users.read', 'roles.manage'] }))
    );
    await open();
    expect(text()).toContain('always holds every permission and cannot be changed');
    expect(text()).toContain('System, locked');

    client.get.mockReturnValue(of(role({ isSystem: true })));
    await open();
    expect(text()).not.toContain('cannot be changed');
    expect(text()).toContain('System');
  });

  it('names a permission the app has no text for by the API sentence, and still shows it', async () => {
    catalogList.mockReturnValue(
      of([...CATALOG, permission('future.thing', PermissionGroup.Documents)])
    );
    client.get.mockReturnValue(of(role({ permissions: ['future.thing'] })));
    await open();

    expect(row('future.thing').textContent).toContain('future.thing help');
    expect(box('future.thing').checked).toBe(true);
  });

  it('asks for the role the URL names, and for nothing when the id is an old numeric bookmark', async () => {
    await open();
    expect(client.get).toHaveBeenCalledWith(ID, expect.anything());

    client.get.mockClear();
    await open('31');

    expect(client.get).not.toHaveBeenCalled();
    expect(spectator.query('h1')?.textContent).toContain('Page not found');
    expect(spectator.query('fieldset')).toBeNull();
  });

  it('a role the API does not know (404) is the not-found page', async () => {
    client.get.mockReturnValue(throwError(() => problem(404, API_ERROR_KIND.NotFound)));
    await open();

    expect(spectator.query('h1')?.textContent).toContain('Page not found');
    expect(spectator.query('fieldset')).toBeNull();
  });

  it('a failed load shows one alert with Retry, and Retry asks again', async () => {
    client.get.mockReturnValue(throwError(() => problem(500, API_ERROR_KIND.Server)));
    await open();
    expect(spectator.queryAll('app-banner[role="alert"]').length).toBe(1);
    expect(spectator.query('fieldset')).toBeNull();

    client.get.mockReturnValue(of(role()));
    spectator.click(spectator.queryAll<HTMLButtonElement>('button')[0]);
    await settle();

    expect(spectator.query('fieldset')).not.toBeNull();
    expect(spectator.queryAll('app-banner[role="alert"]').length).toBe(0);
  });

  it('a page opened after another starts empty: it never shows the role the last page read', async () => {
    await open();
    expect(text()).toContain('Night clerk');

    client.get.mockReturnValue(NEVER);
    await open(OTHER);

    expect(text()).not.toContain('Night clerk');
    expect(spectator.query('fieldset')).toBeNull();
  });
});

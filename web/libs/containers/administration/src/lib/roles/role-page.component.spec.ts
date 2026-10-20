import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { NEVER, of, throwError } from 'rxjs';
import { PermissionDto, PermissionGroup, PermissionsClient, RoleDto, RolesClient } from '@ims/api';
import { provideUiText } from '@ims/core/i18n';
import { API_ERROR_KIND, ApiError } from '@ims/models';
import { RolePageComponent } from './role-page.component';

const ID = '00000000-0000-4000-8000-000000000011';

const permission = (
  code: string,
  group: PermissionGroup,
  extra: Partial<PermissionDto> = {}
): PermissionDto => ({
  code,
  group,
  description: `${code} sentence from the API`,
  isDangerous: false,
  adminCore: false,
  requires: [],
  module: undefined,
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

describe('RolePageComponent (RP02a: a role and what it allows, view only)', () => {
  let spectator: Spectator<RolePageComponent>;
  const client = { get: jest.fn() };
  const catalogList = jest.fn();
  const createComponent = createComponentFactory({
    component: RolePageComponent,
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
  const text = () => spectator.element.textContent ?? '';
  const heading = () => spectator.query('h1')?.textContent?.trim();

  beforeEach(() => {
    jest.resetAllMocks();
    catalogList.mockReturnValue(of(CATALOG));
    client.get.mockReturnValue(of(role()));
  });

  it('lists the permissions by section: a heading per section, each permission Allowed or Not allowed in words', async () => {
    await open();

    expect(heading()).toBe('Night clerk');
    const groups = spectator.queryAll('section h3').map(h => h.firstChild?.textContent?.trim());
    expect(groups).toEqual(['Administration', 'Stock and reports', 'Documents']);
    expect(row('stock.read').textContent).toContain('Allowed');
    expect(row('stock.read').textContent).not.toContain('Not allowed');
    expect(row('document.post').textContent).toContain('Not allowed');
    expect(text()).toContain('1 of 1 on');
  });

  it('is a plain list: no checkbox, no button, and the page has one h1 with h2 cards and h3 sections', async () => {
    await open();

    expect(spectator.queryAll('input').length).toBe(0);
    expect(spectator.queryAll('button').length).toBe(0);
    expect(spectator.queryAll('h1').length).toBe(1);
    expect(spectator.queryAll('h2').map(h => h.textContent?.trim())).toEqual([
      'Details',
      'Permissions'
    ]);
    expect(spectator.queryAll('section h3').length).toBe(3);
  });

  it('shows the translated sentence under a known permission and the API sentence under an unknown one', async () => {
    catalogList.mockReturnValue(
      of([...CATALOG, permission('future.thing', PermissionGroup.Documents)])
    );
    client.get.mockReturnValue(of(role({ permissions: ['stock.read', 'future.thing'] })));
    await open();

    expect(row('stock.read').textContent).toContain(
      'See stock levels, the ledger and the low-stock list.'
    );
    expect(row('stock.read').textContent).not.toContain('sentence from the API');
    expect(row('future.thing').textContent).toContain('future.thing sentence from the API');
    expect(row('future.thing').textContent).toContain('Allowed');
  });

  it('marks high-impact permissions and says what a permission needs, in plain words', async () => {
    await open();

    expect(row('roles.manage').textContent).toContain('High impact');
    expect(row('roles.manage').textContent).toContain('Needs: See users');
    expect(row('stock.read').textContent).not.toContain('High impact');
  });

  it('shows the text, the state and the number of people', async () => {
    await open();

    expect(text()).toContain('Works at night');
    expect(text()).toContain('2 people have this role.');
    expect(text()).toContain('Active');
    expect(text()).toContain('View only');
    expect(text()).toContain('Custom');
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

  it('asks for the role the URL names, and for nothing when the id is an old numeric bookmark', async () => {
    await open();
    expect(client.get).toHaveBeenCalledWith(ID, expect.anything());

    client.get.mockClear();
    await open('31');

    expect(client.get).not.toHaveBeenCalled();
    expect(heading()).toBe('Page not found');
    expect(spectator.query('section')).toBeNull();
  });

  it('a role the API does not know (404) is the not-found page', async () => {
    client.get.mockReturnValue(throwError(() => problem(404, API_ERROR_KIND.NotFound)));
    await open();

    expect(heading()).toBe('Page not found');
    expect(spectator.query('section')).toBeNull();
  });

  it('while it loads, the page has a heading (the list title) and no sentence about a role', async () => {
    client.get.mockReturnValue(NEVER);
    await open();

    expect(heading()).toBe('Roles');
    expect(text()).not.toContain('within about 5 minutes');
    expect(spectator.query('section')).toBeNull();
  });

  it('a failed load keeps a heading, shows one alert with Retry, and Retry asks again', async () => {
    client.get.mockReturnValue(throwError(() => problem(500, API_ERROR_KIND.Server)));
    await open();
    expect(heading()).toBe('Roles');
    expect(spectator.queryAll('app-banner[role="alert"]').length).toBe(1);
    expect(spectator.query('section')).toBeNull();
    expect(text()).not.toContain('within about 5 minutes');

    client.get.mockReturnValue(of(role()));
    spectator.click(spectator.queryAll<HTMLButtonElement>('button')[0]);
    await settle();

    expect(heading()).toBe('Night clerk');
    expect(spectator.queryAll('app-banner[role="alert"]').length).toBe(0);
  });

  describe('what one person read is not shown to the next (stores belong to the page, not the root)', () => {
    it('a page opened for the SAME role after another person, with replies still pending, shows neither the role nor the catalog', async () => {
      await open();
      expect(heading()).toBe('Night clerk');
      expect(row('stock.read')).not.toBeNull();

      // The next person to sign in opens the same role; the answers have not arrived yet.
      client.get.mockReturnValue(NEVER);
      catalogList.mockReturnValue(NEVER);
      await open();

      expect(heading()).toBe('Roles');
      expect(text()).not.toContain('Night clerk');
      expect(spectator.query('[data-permission]')).toBeNull();
      expect(spectator.query('section')).toBeNull();
    });

    it("the second page asks the API again: it does not reuse the first page's answers", async () => {
      await open();
      await open();

      expect(client.get).toHaveBeenCalledTimes(2);
      expect(catalogList).toHaveBeenCalledTimes(2);
    });
  });
});

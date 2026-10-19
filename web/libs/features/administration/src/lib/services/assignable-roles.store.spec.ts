import { HttpContext } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { UsersClient } from '@ims/api';
import { isHandledInline } from '@ims/core/http';
import { AssignableRolesStore } from './assignable-roles.store';

describe('AssignableRolesStore', () => {
  const assignableRoles = jest.fn(() => of([{ code: 'QARoleA', name: 'QA role A' }]));

  beforeEach(() => {
    assignableRoles.mockClear();
    TestBed.configureTestingModule({
      providers: [{ provide: UsersClient, useValue: { assignableRoles } }]
    });
  });

  it('belongs to the users page: nothing provides it from the root, so what one person read is gone with the page', () => {
    expect(() => TestBed.inject(AssignableRolesStore)).toThrow();
  });

  it('asks the API with the failure flagged as handled inline (the page banner is the one announcement), also on reload', async () => {
    TestBed.configureTestingModule({ providers: [AssignableRolesStore] });
    const store = TestBed.inject(AssignableRolesStore);

    store.load();
    await Promise.resolve();
    store.list.reload();
    await Promise.resolve();

    expect(store.list.$data()).toEqual([{ code: 'QARoleA', name: 'QA role A' }]);
    const calls = assignableRoles.mock.calls as unknown as [HttpContext][];
    expect(calls).toHaveLength(2);
    for (const [context] of calls) {
      expect(isHandledInline(context, 500)).toBe(true);
    }
  });
});

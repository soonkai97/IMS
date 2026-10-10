import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  UrlTree,
  convertToParamMap,
  provideRouter
} from '@angular/router';
import { PermissionService } from '@ims/core/identity';
import { needsWarehouseToCreate } from './documents.routes';

describe('needsWarehouseToCreate (EA-14)', () => {
  const warehouseIds = signal<number[]>([]);
  const seesAll = signal(false);

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: PermissionService,
          useValue: { $warehouseIds: warehouseIds, $seesAllWarehouses: seesAll }
        }
      ]
    });
  });

  const run = (id: string) =>
    TestBed.runInInjectionContext(() =>
      needsWarehouseToCreate(
        { paramMap: convertToParamMap({ id }) } as ActivatedRouteSnapshot,
        {} as never
      )
    );

  it('a user with no warehouse is sent Home, where the reason is explained', () => {
    warehouseIds.set([]);
    seesAll.set(false);

    const answer = run('new');

    expect(answer).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(answer as UrlTree)).toBe('/');
  });

  it('a user with a warehouse may open the new form', () => {
    warehouseIds.set([1]);

    expect(run('new')).toBe(true);
  });

  it('Admin and Owner see every warehouse, so they need none assigned', () => {
    warehouseIds.set([]);
    seesAll.set(true);

    expect(run('new')).toBe(true);
  });

  it('a saved document opens whatever the warehouses (the server decides what is visible)', () => {
    warehouseIds.set([]);
    seesAll.set(false);

    expect(run('42')).toBe(true);
  });
});

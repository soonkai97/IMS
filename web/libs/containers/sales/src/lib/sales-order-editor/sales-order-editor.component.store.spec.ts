import { Component, input, output, signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, mockProvider, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { SalesOrderDto, SalesOrderStatus, SalesOrdersClient } from '@ims/api';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { CatalogStore } from '@ims/features/catalog';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import { SalesOrderEditorPageComponent } from './sales-order-editor.component';

const ID = '00000000-0000-4000-8000-000000000001';
const CUSTOMER = '00000000-0000-4000-8000-0000000000a1';
const WAREHOUSE = '00000000-0000-4000-8000-0000000000b1';

@Component({ selector: 'app-product-picker', template: '' })
class StubProductPickerComponent {
  $notice = input<string | undefined>(undefined, { alias: 'notice' });
  $picked = output<PickedProduct>({ alias: 'picked' });
  $termChanged = output<string>({ alias: 'termChanged' });
}

function order(token: string, remark: string | null): SalesOrderDto {
  return {
    id: ID,
    no: 'SO-MAIN-2611-0001',
    status: SalesOrderStatus.Draft,
    customerId: CUSTOMER,
    customerName: 'Acme Trading',
    warehouseId: WAREHOUSE,
    warehouseName: 'Main warehouse',
    remark,
    concurrencyToken: token,
    allowedActions: ['edit', 'submit'],
    lines: [
      {
        lineNo: 1,
        productId: 'p1',
        sku: 'SKU-1',
        productName: 'Widget',
        qty: 10,
        uomId: 'pcs',
        uomCode: 'PCS',
        baseQty: 10,
        unitPrice: 5,
        amount: 50,
        remark: null
      }
    ]
  } as unknown as SalesOrderDto;
}

/** The REAL store with only the HTTP client mocked: the store itself puts the saved order into detail.$data, which re-runs the form fill in the middle of the save. */
describe('SalesOrderEditorPageComponent with the real store', () => {
  let spectator: Spectator<SalesOrderEditorPageComponent>;
  const createComponent = createComponentFactory({
    component: SalesOrderEditorPageComponent,
    detectChanges: false,
    overrideComponents: [
      [
        SalesOrderEditorPageComponent,
        {
          remove: { imports: [ProductPickerComponent] },
          add: { imports: [StubProductPickerComponent] }
        }
      ]
    ],
    providers: [
      provideRouter([]),
      mockProvider(SalesOrdersClient),
      {
        provide: PartnersStore,
        useValue: {
          $partners: signal([{ id: CUSTOMER, name: 'Acme Trading' }]),
          loadActive: jest.fn(),
          list: { reset: jest.fn(), $data: signal(undefined) }
        }
      },
      {
        provide: WarehousesStore,
        useValue: {
          $activeWarehouses: signal([{ id: WAREHOUSE, name: 'Main warehouse' }]),
          ensureLoaded: jest.fn(),
          list: { reset: jest.fn() }
        }
      },
      { provide: CatalogStore, useValue: { $unitCodes: () => new Map() } }
    ]
  });

  it('a save of an opened draft sends the opened version, the answer replaces the opened order, and the form ends clean on its way to the order page', async () => {
    spectator = createComponent({ props: { id: ID } as never });
    const client = spectator.inject(SalesOrdersClient);
    client.get.mockReturnValue(of(order('AAAA', null)));
    client.update.mockReturnValue(of(order('BBBB', 'Call first')));
    const navigate = jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.component.form.controls.remark.setValue('Call first');
    spectator.component.form.controls.remark.markAsDirty();

    spectator.click(spectator.query('.so-editor-bar button') as HTMLElement);
    await spectator.fixture.whenStable();

    expect(client.update.mock.calls[0][0]).toBe(ID);
    expect(client.update.mock.calls[0][1]).toEqual({
      warehouseId: WAREHOUSE,
      customerId: CUSTOMER,
      remark: 'Call first',
      lines: [{ productId: 'p1', qty: 10, uomId: 'pcs', unitPrice: 5, remark: undefined }]
    });
    expect(navigate).toHaveBeenCalledWith(['/', 'sales-orders', ID], { replaceUrl: true });
    expect(spectator.component.$order()?.concurrencyToken).toBe('BBBB');
    expect(spectator.component.form.controls.remark.value).toBe('Call first');
    expect(spectator.component.hasUnsavedChanges()).toBe(false);
  });
});

import { expect, type Locator, type Page } from '@playwright/test';
import { goTo } from './shell';

/** Approvals inbox (S14). */
export class ApprovalsPage {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await goTo(this.page, '/approvals');
    await expect(this.page.getByRole('heading', { level: 1, name: 'Approvals' })).toBeVisible();
  }

  row(docNo: string): Locator {
    return this.page.getByRole('row').filter({ hasText: docNo });
  }

  async tab(name: 'Waiting for me' | 'Submitted by me'): Promise<void> {
    await this.page.getByRole('tab', { name }).click();
  }

  async review(docNo: string): Promise<void> {
    await this.row(docNo).getByRole('link', { name: docNo }).click();
  }
}

/** Stock list, product ledger and low-stock list (S20 to S22). */
export class StockPages {
  constructor(private readonly page: Page) {}

  async openStock(search: string): Promise<void> {
    await goTo(this.page, '/stock');
    await this.page.getByRole('searchbox', { name: 'Search stock' }).fill(search);
  }

  stockRow(sku: string): Locator {
    return this.page.getByRole('row').filter({ hasText: sku });
  }

  async openProductLedger(sku: string): Promise<void> {
    await this.stockRow(sku).getByRole('link', { name: sku }).click();
    await expect(this.page.getByRole('table', { name: 'Stock ledger' })).toBeVisible();
  }

  ledgerRows(): Locator {
    return this.page.getByRole('table', { name: 'Stock ledger' }).getByRole('row');
  }

  async openLowStock(): Promise<void> {
    await goTo(this.page, '/low-stock');
    await expect(this.page.getByRole('heading', { level: 1, name: 'Low stock' })).toBeVisible();
  }

  lowStockRow(sku: string): Locator {
    return this.page.getByRole('row').filter({ hasText: sku });
  }
}

/** Partners (S34). */
export class PartnersPage {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await goTo(this.page, '/partners');
    await expect(this.page.getByRole('heading', { level: 1, name: 'Partners' })).toBeVisible();
  }

  get newButton(): Locator {
    return this.page.getByRole('button', { name: 'New partner' });
  }

  row(name: string): Locator {
    return this.page.getByRole('row').filter({ hasText: name });
  }

  /** Fills the partner dialog and saves; returns the dialog so a test can read server errors from it. */
  async create(type: 'Supplier' | 'Customer', name: string): Promise<Locator> {
    await this.newButton.click();
    const dialog = this.page.getByRole('dialog', { name: 'New partner' });
    await dialog.getByRole('combobox', { name: /^Type/ }).selectOption({ label: type });
    await dialog.getByRole('textbox', { name: /^Name/ }).fill(name);
    await dialog.getByRole('button', { name: 'Save' }).click();
    return dialog;
  }
}

import { expect, type Locator, type Page } from '@playwright/test';
import type { ApiProduct, PublicId } from '../api';
import { Shell, goTo } from './shell';

export type Kind = 'receipts' | 'issues';

/** Receipt and issue lists (S10, S12). */
export class DocumentListPage {
  constructor(
    private readonly page: Page,
    private readonly kind: Kind
  ) {}

  async open(): Promise<void> {
    await goTo(this.page, `/${this.kind}`);
    await expect(this.page.getByRole('heading', { level: 1 })).toBeVisible();
  }

  /** Rows that mention the text (a remark, a number or a partner). */
  rows(text: string | RegExp): Locator {
    return this.page.getByRole('row').filter({ hasText: text });
  }

  async searchFor(text: string): Promise<void> {
    await this.page.getByRole('tab', { name: 'All' }).click();
    const box = this.page.getByRole('searchbox', { name: 'Search documents' });
    await box.clear();
    await box.fill(text);
  }
}

/** The receipt / issue editor and read-only view (S11, S13), including the Add item sheet and the confirm dialogs. */
export class DocumentEditor {
  readonly shell: Shell;
  readonly heading: Locator;
  readonly warehouse: Locator;
  readonly partner: Locator;
  readonly remark: Locator;
  readonly addItemButton: Locator;
  /** The status chip next to the title (Draft, Submitted, Posted ...). */
  readonly status: Locator;
  readonly staleBanner: Locator;

  constructor(private readonly page: Page) {
    this.shell = new Shell(page);
    this.heading = page.getByRole('heading', { level: 1 });
    this.warehouse = page.getByRole('combobox', { name: /^Warehouse/ });
    this.partner = page.getByRole('combobox', { name: /^(Supplier|Customer)/ });
    this.remark = page.getByRole('textbox', { name: 'Remark' });
    this.addItemButton = page.getByRole('main').getByRole('button', { name: 'Add item' });
    this.status = page.locator('app-page-header app-status-chip').first();
    this.staleBanner = page.getByRole('alert').filter({ hasText: 'changed since you opened it' });
  }

  async openNew(kind: Kind): Promise<void> {
    await goTo(this.page, `/${kind}/new`);
    await expect(this.page.getByRole('heading', { level: 1, name: /^New / })).toBeVisible();
  }

  async open(kind: Kind, id: PublicId): Promise<void> {
    await goTo(this.page, `/${kind}/${id}`);
    await expect(this.status).toBeVisible();
  }

  /** The id of the saved document, read from the address once the first save moved /new to /<id>. */
  async savedId(): Promise<PublicId> {
    await expect(this.page).toHaveURL(/\/(receipts|issues)\/[0-9a-f-]{36}$/);
    return new URL(this.page.url()).pathname.split('/').pop() ?? '';
  }

  async chooseWarehouse(name: string): Promise<void> {
    await this.warehouse.selectOption({ label: name });
  }

  async choosePurpose(label: 'Sale' | 'Use' | 'Scrap'): Promise<void> {
    await this.page.getByRole('radio', { name: label }).check();
  }

  /** Adds a product through the Add item sheet by typing `term` (SKU, name or barcode) and tapping the result. */
  async addItem(product: Pick<ApiProduct, 'sku' | 'name'>, term = product.sku): Promise<void> {
    const sheet = await this.openAddItem();
    await sheet.getByRole('searchbox', { name: 'Find a product' }).fill(term);
    await sheet.getByRole('button', { name: `${product.sku}, ${product.name}` }).click();
    await expect(sheet.getByText(`${product.sku} added.`)).toBeVisible();
    await sheet.getByRole('button', { name: 'Done' }).click();
    await expect(sheet).toBeHidden();
  }

  async openAddItem(): Promise<Locator> {
    await this.addItemButton.click();
    const sheet = this.shell.dialog('Add item');
    await expect(sheet).toBeVisible();
    return sheet;
  }

  line(sku: string): Locator {
    return this.page
      .getByRole('region', { name: 'Lines' })
      .getByRole('listitem')
      .filter({ hasText: sku });
  }

  qty(sku: string): Locator {
    return this.page.getByRole('spinbutton', { name: `Quantity of ${sku}` });
  }

  async setQty(sku: string, qty: number): Promise<void> {
    await this.qty(sku).fill(String(qty));
  }

  /** The unit cost input of a receipt line: there only for a user with cost.view (Clerk, Supervisor, Owner, Admin). */
  unitCost(sku: string): Locator {
    return this.page.getByRole('spinbutton', { name: `Unit cost of ${sku}` });
  }

  async setUnitCost(sku: string, cost: number): Promise<void> {
    await this.unitCost(sku).fill(String(cost));
  }

  /** A document action by its exact name (Save draft, Submit & post, Post, Approve, Reverse ...). */
  action(name: string): Locator {
    return this.page.getByRole('button', { name, exact: true });
  }

  /** Submit & post is one question asked up front; after the answer the document is submitted and posted with no second dialog. */
  async submitAndPost(): Promise<void> {
    await this.action('Submit & post').click();
    await this.confirm('Submit & post?', 'Submit & post');
  }

  /** Answers the confirm dialog that follows an action: optional reason, then the confirm button. */
  async confirm(title: string | RegExp, button: string, reason?: string): Promise<void> {
    const dialog = this.shell.dialog(title);
    await expect(dialog).toBeVisible();
    if (reason !== undefined) {
      await dialog.getByRole('textbox').fill(reason);
    }
    await dialog.getByRole('button', { name: button, exact: true }).click();
  }
}

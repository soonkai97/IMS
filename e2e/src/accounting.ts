import { apiAs, type ApiPartner, type ApiProduct, type PublicId } from './api';

/**
 * Helpers of the accounting scenarios (`40-accounting`, `41-accounting-export`): the module state of the installation is read first
 * and put back last, and the rows a scenario made are throw-away rows found again by their own name prefixes.
 */

/** The SKU and the partner name prefixes of these scenarios: the clean-up finds its rows by them, also after a run that stopped half way. */
export const SKU_PREFIX = 'E2E-ACC-';
export const PARTNER_PREFIX = 'E2E Acct Supplier ';

/** The first calls after the API started are slow (cold queries and a lock): allow 30 s for them. */
export const SLOW = 30_000;

export interface ProductRow extends ApiProduct {
  categoryId: PublicId | null;
  baseUomId: PublicId;
  safetyStock: number;
  isActive: boolean;
  conversions: { fromUomId: PublicId; toUomId: PublicId; factor: number }[];
  hsCode?: string;
  sstCode?: string;
}

export interface PartnerRow extends ApiPartner {
  type: 'Supplier' | 'Customer';
  contact?: string;
  isActive: boolean;
  tin?: string;
  sstRegNo?: string;
}

const admin = () => apiAs('admin');

export async function accountingIsOn(): Promise<boolean> {
  const list = await admin().call<{ code: string; enabled: boolean }[]>('GET', '/api/modules');
  return list.find(item => item.code === 'accounting')?.enabled === true;
}

/** Puts the module in the state asked for (a call that finds it there already does nothing, so nothing is audited twice). */
export async function setAccounting(enabled: boolean, reason: string): Promise<void> {
  if ((await accountingIsOn()) !== enabled) {
    await admin().call('PUT', '/api/modules/accounting', { enabled, reason });
  }
}

/** Today in Kuala Lumpur as a user types it (dd/MM/yyyy). */
export const today = (): string =>
  new Date().toLocaleDateString('en-GB', { timeZone: 'Asia/Kuala_Lumpur' });

/**
 * Clears the tax values of every throw-away row of these scenarios and deactivates it. The module must be on while this runs (off,
 * a save leaves the stored values alone and they would stay).
 */
export async function retireThrowAways(): Promise<void> {
  const products = await admin().call<{ items: ProductRow[] }>(
    'GET',
    `/api/products?search=${encodeURIComponent(SKU_PREFIX)}&pageSize=100`
  );
  for (const product of products.items.filter(item => item.sku.startsWith(SKU_PREFIX))) {
    await admin().call('PUT', `/api/products/${product.id}`, {
      ...product,
      isActive: false,
      hsCode: '',
      sstCode: ''
    });
  }
  const partners = await admin().call<{ items: PartnerRow[] }>(
    'GET',
    `/api/partners?search=${encodeURIComponent(PARTNER_PREFIX)}&includeInactive=true&pageSize=100`
  );
  for (const partner of partners.items.filter(item => item.name.startsWith(PARTNER_PREFIX))) {
    await admin().call('PUT', `/api/partners/${partner.id}`, {
      ...partner,
      isActive: false,
      tin: '',
      sstRegNo: ''
    });
  }
}

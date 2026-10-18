/**
 * Test data through the public API (never the database). Every call signs in with the real login endpoint using the demo
 * password of a role; the access token stays in memory. Data is created with unique names so scenarios stay independent.
 */
import { randomBytes, randomInt } from 'node:crypto';
import { API_URL, passwordFor, type Role } from './config';
import { spendLoginBudget } from './rate-limit';

/** The id of an entity on the wire: an opaque GUID string, never parsed or compared as a number. */
export type PublicId = string;

export interface ApiDocument {
  id: PublicId;
  no: string;
  status: string;
  requiresApproval: boolean;
  requiresOverride: boolean;
  concurrencyToken: string;
  allowedActions: string[];
  lines: { productId: PublicId; sku: string; qty: number }[];
}
export interface ApiProduct {
  id: PublicId;
  sku: string;
  name: string;
  barcodes: string[];
}
export interface ApiRole {
  id: PublicId;
  code: string;
  name: string;
  isSystem: boolean;
  permissions: string[];
  userCount: number;
  concurrencyToken: string;
}

export interface ApiPartner {
  id: PublicId;
  name: string;
}
export interface DocumentLine {
  productId: PublicId;
  qty: number;
  unitCost?: number;
}

const tokens = new Map<Role, string>();
let mainWarehouseId: PublicId | undefined;
let counter = 0;

/** A short unique, upper-case token for names, SKUs and barcodes. */
export const unique = (): string =>
  `${Date.now().toString(36)}${(counter++).toString(36)}${randomInt(0, 1296).toString(36)}`.toUpperCase();

/** Signs in; sign-in is limited to 20 a minute per address, so a 429 waits as long as the API asks. */
async function signIn(role: Role): Promise<string> {
  for (let attempt = 1; ; attempt++) {
    await spendLoginBudget(1);
    const response = await fetch(`${API_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: role, password: passwordFor(role) })
    });
    if (response.status === 429 && attempt < 4) {
      const wait = Number(response.headers.get('Retry-After')) || 30;
      await new Promise(resolve => setTimeout(resolve, Math.min(wait, 65) * 1000));
      continue;
    }
    if (!response.ok) {
      throw new Error(`API sign-in as ${role} failed (HTTP ${response.status}).`);
    }
    const token = ((await response.json()) as { accessToken: string }).accessToken;
    tokens.set(role, token);
    return token;
  }
}

const DOCUMENT_COMMAND = /^\/api\/documents\/[0-9a-f-]{36}(\/(submit|approve|reject|recall|cancel|post|reverse))?$/;

export class ImsApi {
  constructor(private readonly role: Role) {}

  /** One request; signs in on first use and once more when the token has expired. Returns the status, never throws on 4xx. */
  async raw<T = unknown>(
    method: string,
    path: string,
    body?: unknown
  ): Promise<{ status: number; body: T }> {
    const needsIfMatch = DOCUMENT_COMMAND.test(path) && method !== 'GET';
    for (const attempt of [0, 1]) {
      const token = tokens.get(this.role) ?? (await signIn(this.role));
      const response = await fetch(`${API_URL}${path}`, {
        method,
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
          ...(needsIfMatch ? { 'If-Match': '*' } : {})
        },
        body: body === undefined ? undefined : JSON.stringify(body)
      });
      if (response.status === 401 && attempt === 0) {
        tokens.delete(this.role);
        continue;
      }
      const text = await response.text();
      return { status: response.status, body: (text ? JSON.parse(text) : null) as T };
    }
    throw new Error('unreachable');
  }

  /** Like raw, but a non-2xx answer fails the test with the server's sentence. */
  async call<T = unknown>(method: string, path: string, body?: unknown): Promise<T> {
    const result = await this.raw<T>(method, path, body);
    if (result.status >= 300) {
      const detail = (result.body as { detail?: string } | null)?.detail ?? '';
      throw new Error(`${method} ${path} as ${this.role}: HTTP ${result.status} ${detail}`);
    }
    return result.body;
  }

  // Documents ----------------------------------------------------------------------------------------------------

  /** The seeded "Main warehouse", where the demo staff, supervisor and clerk work. */
  private async warehouseId(): Promise<PublicId> {
    if (mainWarehouseId === undefined) {
      const list = await new ImsApi('admin').call<{ id: PublicId; name: string }[]>(
        'GET',
        '/api/warehouses'
      );
      mainWarehouseId = (list.find(w => /main/i.test(w.name)) ?? list[0])!.id;
    }
    return mainWarehouseId;
  }

  async createDocument(
    kind: 'receipts' | 'issues',
    lines: DocumentLine[],
    options: { partnerId?: PublicId | null; remark?: string; warehouseId?: PublicId } = {}
  ): Promise<ApiDocument> {
    return this.call<ApiDocument>('POST', `/api/documents/${kind}`, {
      warehouseId: options.warehouseId ?? (await this.warehouseId()),
      partnerId: options.partnerId ?? null,
      purpose: kind === 'issues' ? 'Sale' : null,
      remark: options.remark ?? null,
      lines
    });
  }

  command(
    id: PublicId,
    name: 'submit' | 'approve' | 'recall' | 'post' | 'cancel',
    body?: object
  ): Promise<ApiDocument> {
    return this.call<ApiDocument>('POST', `/api/documents/${id}/${name}`, body ?? {});
  }

  reverse(id: PublicId, reason: string): Promise<ApiDocument> {
    return this.call<ApiDocument>('POST', `/api/documents/${id}/reverse`, { reason });
  }

  /** Create, submit and post a document in one go (only for documents that need no approval). */
  async post(kind: 'receipts' | 'issues', lines: DocumentLine[]): Promise<ApiDocument> {
    const draft = await this.createDocument(kind, lines);
    await this.command(draft.id, 'submit');
    return this.command(draft.id, 'post');
  }

  getDocument(id: PublicId): Promise<ApiDocument> {
    return this.call<ApiDocument>('GET', `/api/documents/${id}`);
  }

  /** How many documents of a kind the signed-in user created (any status, or one status). */
  async countMine(kind: 'receipts' | 'issues', status?: string): Promise<number> {
    const type = kind === 'receipts' ? 'Receipt' : 'Issue';
    const query = `Type=${type}&Mine=true&PageSize=1&IncludeTotal=true${status ? `&Status=${status}` : ''}`;
    return (await this.call<{ total: number }>('GET', `/api/documents?${query}`)).total;
  }

  // Master data and stock ----------------------------------------------------------------------------------------

  async createProduct(
    options: { safetyStock?: number; isActive?: boolean; name?: string; sku?: string } = {}
  ): Promise<ApiProduct> {
    const token = unique();
    const units = await this.call<{ id: PublicId; code: string }[]>('GET', '/api/units');
    return this.call<ApiProduct>('POST', '/api/products', {
      sku: options.sku ?? `E2E-${token}`,
      name: options.name ?? `E2E Widget ${token}`,
      categoryId: null,
      baseUomId: (units.find(u => u.code === 'PCS') ?? units[0])!.id,
      safetyStock: options.safetyStock ?? 0,
      isActive: options.isActive ?? true,
      // 13 digits: the counter is in them (cut off before, so two products made within 10 ms shared a barcode).
      barcodes: [`9${String(Date.now()).slice(-8)}${String(counter).padStart(4, '0')}`],
      conversions: []
    });
  }

  async createPartner(
    type: 'Supplier' | 'Customer',
    options: { name?: string; contact?: string } = {}
  ): Promise<ApiPartner> {
    return this.call<ApiPartner>('POST', '/api/partners', {
      type,
      name: options.name ?? `E2E ${type} ${unique()}`,
      contact: options.contact ?? null,
      isActive: true
    });
  }

  /** A warehouse nobody but Admin and Owner is assigned to (the demo staff, supervisor and clerk work in the Main warehouse only). */
  async createWarehouse(): Promise<{ id: PublicId; name: string }> {
    const token = unique();
    return this.call<{ id: PublicId; name: string }>('POST', '/api/warehouses', {
      code: `E${token}`.slice(0, 20),
      name: `E2E Warehouse ${token}`,
      contact: null,
      notes: null,
      isActive: true
    });
  }

  /** A throw-away Clerk with a made-up password, so a wrong-password test never touches a demo account. */
  async createUser(
    options: { displayName?: string; role?: string } = {}
  ): Promise<{ username: string; password: string }> {
    const token = unique().toLowerCase();
    const user = {
      username: `e2e-${token}`,
      password: `Aa1!${randomBytes(9).toString('base64url')}-xYz`
    };
    await this.call('POST', '/api/users', {
      username: user.username,
      email: `${user.username}@example.test`,
      displayName: options.displayName ?? `E2E ${token}`,
      role: options.role ?? 'Clerk',
      isActive: true,
      password: user.password,
      warehouseIds: []
    });
    return user;
  }

  /** A product with stock on hand, received and posted through the API. */
  async stockedProduct(
    qty: number,
    unitCost = 1,
    options: { safetyStock?: number } = {}
  ): Promise<ApiProduct> {
    const product = await this.createProduct(options);
    await this.post('receipts', [{ productId: product.id, qty, unitCost }]);
    return product;
  }

  async onHand(productId: PublicId): Promise<number> {
    const page = await this.call<{ items: { qtyOnHand: number }[] }>(
      'GET',
      `/api/stock?productId=${productId}`
    );
    return page.items.reduce((sum, row) => sum + Number(row.qtyOnHand), 0);
  }

  // Roles -------------------------------------------------------------------------------------------------------

  /** A custom role holding exactly these permissions (admin only). */
  async createRole(name: string, permissions: string[]): Promise<ApiRole> {
    return this.call<ApiRole>('POST', '/api/roles', { name, description: '', permissions });
  }

  // Settings -----------------------------------------------------------------------------------------------------

  /** Switch "allow negative stock" (admin only). Returns a function that puts the previous settings back. */
  async setNegativeStock(allowed: boolean): Promise<() => Promise<void>> {
    const before = await this.call<Record<string, unknown>>('GET', '/api/settings');
    await this.call('PUT', '/api/settings', { ...before, allowNegativeStock: allowed });
    return async () => {
      await this.call('PUT', '/api/settings', before);
    };
  }
}

export const apiAs = (role: Role): ImsApi => new ImsApi(role);

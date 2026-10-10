/** Filter-independent part of every list request: load(filter, page, pageSize, sort). */
export interface PagedQuery {
  /** 1-based. */
  readonly page: number;
  readonly pageSize: number;
  readonly sortBy?: string;
  readonly desc?: boolean;
}

/** The server's PagedResult envelope (total is -1 when the count was skipped). */
export interface PagedResult<Item> {
  readonly items: Item[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

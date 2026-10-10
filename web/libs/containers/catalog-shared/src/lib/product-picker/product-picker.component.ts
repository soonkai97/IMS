import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  EMPTY,
  catchError,
  debounceTime,
  distinctUntilChanged,
  filter,
  map,
  of,
  switchMap,
  tap
} from 'rxjs';
import { ProductDto, ProductLookupResult } from '@ims/api';
import { AppQtyPipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { ProductsStore } from '@ims/features/catalog';
import { StockAvailabilityService } from '@ims/features/documents';
import { MIN_SEARCH_LENGTH, SEARCH_DEBOUNCE_MS } from '@ims/models';
import { UiSearchInputComponent, UiSkeletonComponent } from '@ims/ui';

export interface PickedProduct {
  readonly product: ProductDto;
  /** On-hand quantity in the base unit for the picker's warehouse; null when no warehouse was given. */
  readonly available: number | null;
}

interface ResultItem {
  readonly product: ProductDto;
  readonly available: number | null;
}

/**
 * Product type-ahead: SKU, name or barcode after 2 characters (300 ms debounce). Enter on an exact barcode / SKU
 * picks that product straight away, so a keyboard-wedge scanner works without a camera (D8).
 */
@Component({
  selector: 'app-product-picker',
  imports: [AppQtyPipe, TranslatePipe, UiSearchInputComponent, UiSkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-picker.component.html',
  styleUrl: './product-picker.component.scss'
})
export class ProductPickerComponent {
  private readonly availability = inject(StockAvailabilityService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly products = inject(ProductsStore);

  /** When set, results show how much of each product is on hand there (issues). */
  $warehouseId = input<number | null>(null, { alias: 'warehouseId' });

  $picked = output<PickedProduct>({ alias: 'picked' });

  $term = signal('');
  /** Result of the last exact-code lookup when it picked nothing ("no product has the code ..."). */
  $lookupMessage = signal<string | undefined>(undefined);
  $results = signal<readonly ResultItem[]>([]);
  $isSearching = signal(false);
  $showAvailable = computed(() => this.$warehouseId() !== null);
  $hasSearched = computed(
    () => this.$term().trim().length >= MIN_SEARCH_LENGTH && !this.$isSearching()
  );

  constructor() {
    toObservable(this.$term)
      .pipe(
        tap(() => this.$lookupMessage.set(undefined)),
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        tap(term => this.$isSearching.set(term.trim().length >= MIN_SEARCH_LENGTH)),
        switchMap(term => this.search(term.trim())),
        tap(results => {
          this.$results.set(results);
          this.$isSearching.set(false);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** Enter / scanner: an exact SKU or barcode. A miss is an answer, not an error: say so quietly, no toast. */
  onSubmitted(code: string): void {
    this.$lookupMessage.set(undefined);
    this.products
      .lookup(code)
      .pipe(
        catchError(() => EMPTY),
        map(result => this.usableProduct(code, result)),
        filter((product): product is ProductDto => product !== undefined),
        switchMap(product => this.withAvailability(product)),
        tap(item => {
          this.$picked.emit(item);
          this.$term.set('');
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  onPick(item: ResultItem): void {
    this.$picked.emit({ product: item.product, available: item.available });
  }

  private search(term: string) {
    if (term.length < MIN_SEARCH_LENGTH) {
      return of<readonly ResultItem[]>([]);
    }
    const warehouseId = this.$warehouseId();
    return this.products.search(term).pipe(
      switchMap(page =>
        warehouseId === null
          ? of(page.items.map(product => ({ product, available: null })))
          : this.availability
              .bySearch(warehouseId, term)
              .pipe(
                switchMap(stock =>
                  of(
                    page.items.map(product => ({ product, available: stock.get(product.id) ?? 0 }))
                  )
                )
              )
      ),
      catchError(() => of<readonly ResultItem[]>([]))
    );
  }

  /** `{ found: false }` and deactivated products give the user a sentence instead of a pick. */
  private usableProduct(code: string, result: ProductLookupResult): ProductDto | undefined {
    const product = result.found ? result.product : undefined;
    if (!product) {
      this.$lookupMessage.set(this.i18n.t('picker.notFound', { code }));
      return undefined;
    }
    if (!product.isActive) {
      this.$lookupMessage.set(this.i18n.t('picker.inactive', { sku: product.sku }));
      return undefined;
    }
    return product;
  }

  private withAvailability(product: ProductDto) {
    const warehouseId = this.$warehouseId();
    if (warehouseId === null) {
      return of<PickedProduct>({ product, available: null });
    }
    return this.availability
      .forProduct(warehouseId, product.id)
      .pipe(switchMap(available => of<PickedProduct>({ product, available })));
  }
}

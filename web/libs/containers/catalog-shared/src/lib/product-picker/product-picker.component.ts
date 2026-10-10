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
import { catchError, debounceTime, distinctUntilChanged, filter, of, switchMap, tap } from 'rxjs';
import { ProductDto } from '@ims/api';
import { AppQtyPipe, TranslatePipe } from '@ims/core/i18n';
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
  private readonly products = inject(ProductsStore);

  /** When set, results show how much of each product is on hand there (issues). */
  $warehouseId = input<number | null>(null, { alias: 'warehouseId' });

  $picked = output<PickedProduct>({ alias: 'picked' });

  $term = signal('');
  $results = signal<readonly ResultItem[]>([]);
  $isSearching = signal(false);
  $showAvailable = computed(() => this.$warehouseId() !== null);
  $hasSearched = computed(
    () => this.$term().trim().length >= MIN_SEARCH_LENGTH && !this.$isSearching()
  );

  constructor() {
    toObservable(this.$term)
      .pipe(
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

  onSubmitted(code: string): void {
    this.products
      .lookup(code)
      .pipe(
        catchError(() => of(undefined)),
        filter((product): product is ProductDto => product !== undefined && product.isActive),
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

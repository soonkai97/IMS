import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';
import { map, startWith, tap } from 'rxjs';
import { DocumentDto, DocumentSaveRequest, DocumentType, IssuePurpose, ProductDto } from '@ims/api';
import { CatalogStore } from '@ims/features/catalog';
import {
  LineGroup,
  LineValue,
  LinesArray,
  StockAvailabilityService,
  computeTotals,
  createLineGroup,
  lineFromDocument,
  lineFromProduct,
  maxCoveredQuantity,
  toLineInputs
} from '@ims/features/documents';
import { MAX_DOCUMENT_LINES, NO_PUBLIC_ID, PublicId } from '@ims/models';
import { Observable } from 'rxjs';

/** The API refuses a longer remark; the form must not offer more than it accepts (EA-18). */
const MAX_REMARK_LENGTH = 500;
const QUANTITY_STEP = 1;
const MIN_STEPPED_QUANTITY = 0.001;

/** What `addProduct` did: a new line, or one more of an existing line. */
export interface AddedLine {
  readonly increased: boolean;
  /** Quantity of the line after the add, in the line's unit. */
  readonly qty: number;
}

/**
 * Form model of the document editor (header + lines) and the numbers derived from it. One instance per editor page.
 * Keeping it out of the component makes the line logic testable without a DOM.
 */
@Injectable()
export class DocumentEditorViewService {
  private readonly availability = inject(StockAvailabilityService);
  private readonly catalog = inject(CatalogStore);

  readonly header = new FormGroup({
    warehouseId: new FormControl<PublicId | null>(null, [Validators.required]),
    partnerId: new FormControl<PublicId | null>(null),
    purpose: new FormControl<IssuePurpose | null>(null),
    remark: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_REMARK_LENGTH)]
    })
  });
  readonly lines: LinesArray = new FormArray<LineGroup>([]);

  /** Warehouse the `available` figures of the lines belong to; null = not known. */
  private availabilityWarehouseId: PublicId | null = null;

  readonly $type = signal<DocumentType>(DocumentType.Receipt);
  readonly $values = toSignal(
    this.lines.valueChanges.pipe(
      startWith(null),
      map(() => this.lines.getRawValue())
    ),
    { initialValue: [] as LineValue[] }
  );
  readonly $totals = computed(() => computeTotals(this.$values(), this.$type()));
  readonly $canAddLine = computed(() => this.$values().length < MAX_DOCUMENT_LINES);

  /** Sets the document type: issues need a purpose, receipts do not. */
  configure(type: DocumentType): void {
    this.$type.set(type);
    this.header.controls.purpose.setValidators(
      type === DocumentType.Issue ? [Validators.required] : []
    );
    this.header.controls.purpose.updateValueAndValidity({ emitEvent: false });
  }

  /** Empty form for a new document; a single warehouse is pre-selected. */
  startNew(defaultWarehouseId: PublicId | null): void {
    this.header.reset({
      warehouseId: defaultWarehouseId,
      partnerId: null,
      purpose: null,
      remark: ''
    });
    this.lines.clear();
    this.header.markAsPristine();
    this.header.enable();
  }

  populate(document: DocumentDto, editable: boolean): void {
    this.availabilityWarehouseId = document.warehouseId;
    this.header.reset({
      warehouseId: document.warehouseId,
      partnerId: document.partnerId ?? null,
      purpose: document.purpose ?? null,
      remark: document.remark ?? ''
    });
    this.lines.clear();
    for (const line of document.lines) {
      this.lines.push(createLineGroup(lineFromDocument(line)));
    }
    if (editable) {
      this.header.enable();
      this.lines.enable();
    } else {
      this.header.disable();
      this.lines.disable();
    }
    this.header.markAsPristine();
    this.lines.markAsPristine();
  }

  /**
   * Adds the product, or raises the quantity of the line it is already on (repeated scans accumulate). Says which of
   * the two happened and the quantity now, so the page can tell the user (NV-04); undefined = the document is full.
   */
  addProduct(product: ProductDto, available: number | null): AddedLine | undefined {
    const existing = this.lines.controls.find(
      group => group.controls.productId.value === product.id
    );
    if (existing) {
      this.step(existing, QUANTITY_STEP);
      existing.controls.available.setValue(available ?? existing.controls.available.value);
      return { increased: true, qty: existing.controls.qty.value ?? 0 };
    }
    if (!this.$canAddLine()) {
      return undefined;
    }
    const line = lineFromProduct(product, this.catalog.$unitCodes(), available);
    this.lines.push(createLineGroup(line));
    this.lines.markAsDirty();
    return { increased: false, qty: line.qty ?? 0 };
  }

  /** The form now mirrors what the server holds (after a save): leaving the page must not ask "Discard changes?". */
  markPristine(): void {
    this.header.markAsPristine();
    this.lines.markAsPristine();
  }

  step(group: LineGroup, delta: number): void {
    const next = Math.max(MIN_STEPPED_QUANTITY, (group.controls.qty.value ?? 0) + delta);
    group.controls.qty.setValue(next);
    group.controls.qty.markAsDirty();
  }

  remove(index: number): void {
    this.lines.removeAt(index);
    this.lines.markAsDirty();
  }

  /** E1 one-tap fix: lower the line to what the warehouse can cover. */
  setToAvailable(group: LineGroup): void {
    group.controls.qty.setValue(maxCoveredQuantity(group.getRawValue()));
    group.controls.qty.markAsDirty();
  }

  /**
   * True when the `available` figures of the lines do not belong to this warehouse. A loaded document brings its own
   * figures (they are those of its warehouse), so only a changed warehouse with lines on the form needs a stock query.
   */
  needsAvailability(warehouseId: PublicId): boolean {
    return this.lines.length > 0 && warehouseId !== this.availabilityWarehouseId;
  }

  /** Re-reads on-hand quantities of an issue's lines after the warehouse changed. */
  refreshAvailability(warehouseId: PublicId): Observable<ReadonlyMap<PublicId, number>> {
    return this.availability.bySearch(warehouseId, '').pipe(
      tap(stock => {
        for (const group of this.lines.controls) {
          group.controls.available.setValue(stock.get(group.controls.productId.value) ?? 0);
        }
        this.availabilityWarehouseId = warehouseId;
      })
    );
  }

  clearAvailability(): void {
    for (const group of this.lines.controls) {
      group.controls.available.setValue(null);
    }
  }

  hasUnsavedChanges(): boolean {
    return this.header.dirty || this.lines.dirty;
  }

  isValid(): boolean {
    return this.header.valid && this.lines.valid && this.lines.length > 0;
  }

  markAllTouched(): void {
    this.header.markAllAsTouched();
    this.lines.markAllAsTouched();
  }

  toRequest(): DocumentSaveRequest {
    const header = this.header.getRawValue();
    const type = this.$type();
    return {
      warehouseId: header.warehouseId ?? NO_PUBLIC_ID,
      partnerId: header.partnerId ?? undefined,
      purpose: type === DocumentType.Issue ? (header.purpose ?? undefined) : undefined,
      remark: header.remark.trim().length > 0 ? header.remark.trim() : undefined,
      lines: toLineInputs(this.lines.getRawValue(), type)
    };
  }
}

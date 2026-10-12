export interface DelayedLoadingOptions {
  /** How long the request must be pending before the indicator shows. */
  readonly delay?: number;
  /** How long the indicator stays once it showed. */
  readonly minShow?: number;
}

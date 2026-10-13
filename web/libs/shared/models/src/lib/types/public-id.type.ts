/**
 * The id of an entity on the wire: an opaque GUID string (ADR guid-keys). Never parsed, never compared numerically,
 * never sorted for meaning. The ledger and audit row ids are positions, not entity ids, and stay numbers.
 */
export type PublicId = string;

import { PublicId } from '../types/public-id.type';

/**
 * Stands in for a required id that validation has already proved present, where a request type wants a value. It is
 * never sent in practice, and if it were the API would refuse it (400), so it can never name a row.
 */
export const NO_PUBLIC_ID: PublicId = '';

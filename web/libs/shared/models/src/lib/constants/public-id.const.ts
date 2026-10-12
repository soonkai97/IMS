import { PublicId } from '../types/public-id.type';

/**
 * Stands in for a required id that validation has already proved present, where a request type wants a value. The API
 * answers it with the same 400 an unknown id gets, so it can never name a row.
 */
export const NO_PUBLIC_ID: PublicId = '';

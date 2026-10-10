import { API_ERROR_KIND, API_STATUS } from '../constants';
import { ApiError } from './api-error.type';

export type ApiStatus = (typeof API_STATUS)[keyof typeof API_STATUS];

/** State of one API call. Stores expose one per call (list, save, post...) as a signal. */
export interface ApiOperation {
  readonly status: ApiStatus;
  readonly error?: ApiError;
}

export const IDLE_OPERATION: ApiOperation = { status: API_STATUS.Idle };
export const LOADING_OPERATION: ApiOperation = { status: API_STATUS.Loading };
export const SUCCESS_OPERATION: ApiOperation = { status: API_STATUS.Success };

export function failureOperation(error: ApiError): ApiOperation {
  return { status: API_STATUS.Failure, error };
}

export function isLoading(operation: ApiOperation): boolean {
  return operation.status === API_STATUS.Loading;
}

export function isFailure(operation: ApiOperation): boolean {
  return operation.status === API_STATUS.Failure;
}

/** Error used when something other than an HTTP error is thrown. */
export const UNKNOWN_ERROR: ApiError = {
  isApiError: true,
  kind: API_ERROR_KIND.Unknown,
  status: 0,
  title: 'Error',
  detail: '',
  fieldErrors: {},
  extensions: {}
};

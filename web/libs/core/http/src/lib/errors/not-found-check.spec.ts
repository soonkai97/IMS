import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { UNKNOWN_ROUTE_DETAIL } from './not-found-check';

describe('UNKNOWN_ROUTE_DETAIL', () => {
  it('is the text the API writes for an address that names no endpoint (Messages.RouteNotFound)', () => {
    const messages = readFileSync(
      resolve(__dirname, '../../../../../../../src/Ims.Domain/Common/Constants/Messages.cs'),
      'utf8'
    );

    expect(messages).toContain(`RouteNotFound = "${UNKNOWN_ROUTE_DETAIL}"`);
  });
});

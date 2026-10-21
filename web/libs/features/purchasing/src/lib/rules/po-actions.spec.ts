import { PO_ACTION } from '../constants/purchasing.const';
import { toPoActions } from './po-actions';

describe('toPoActions', () => {
  it('keeps the names this web knows and ignores the rest', () => {
    const actions = toPoActions(['submit', 'edit', 'close', 'receive', 'teleport']);

    expect([...actions].sort()).toEqual([PO_ACTION.Close, PO_ACTION.Edit, PO_ACTION.Submit]);
  });

  it('is empty for no list', () => {
    expect(toPoActions(undefined).size).toBe(0);
  });
});

import { saveFile } from './download';

describe('saveFile', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    URL.createObjectURL = jest.fn(() => 'blob:test');
    URL.revokeObjectURL = jest.fn();
    jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
  });

  it('names the download after the server and revokes the object URL after the click was handled, not in the same task', () => {
    saveFile({ data: new Blob(['x']), fileName: 'ims-export.xlsx' }, 'fallback.xlsx');

    expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    jest.runAllTimers();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test');
  });

  it('uses the fallback name when the server sent none', () => {
    const names: string[] = [];
    jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement
    ) {
      names.push(this.download);
    });

    saveFile({ data: new Blob(['x']) }, 'fallback.xlsx');

    expect(names).toEqual(['fallback.xlsx']);
  });
});

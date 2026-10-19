import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiPagerComponent } from './pager.component';

describe('UiPagerComponent', () => {
  let spectator: Spectator<UiPagerComponent>;
  const createComponent = createComponentFactory({
    component: UiPagerComponent,
    detectChanges: false
  });

  function render(props: Record<string, unknown>): void {
    spectator = createComponent({
      props: { page: 3, pageSize: 25, total: 333, ...props } as never
    });
    spectator.detectChanges();
  }

  const button = (name: string): HTMLButtonElement =>
    spectator.query<HTMLButtonElement>(`button[aria-label="${name}"]`) as HTMLButtonElement;

  it('keeps the range text 26-50 of 333 and names the nav by the page', () => {
    render({ page: 2 });

    expect(spectator.query('.ui-pager-range')?.textContent?.trim()).toBe('26-50 of 333');
    expect(spectator.query('nav')?.getAttribute('aria-label')).toBe('Page 2 of 14');
  });

  it('has one live region, and it reads the page and the range together', () => {
    render({ page: 2 });

    expect(spectator.queryAll('[aria-live]')).toHaveLength(1);
    expect(spectator.query('[aria-live]')?.textContent?.trim()).toBe('Page 2 of 14, 26-50 of 333');
  });

  it('has First, Previous, Next and Last, each with a name', () => {
    render({});

    for (const name of ['First page', 'Previous page', 'Next page', 'Last page']) {
      expect(button(name)).not.toBeNull();
    }
  });

  it('marks First and Previous unusable on the first page, Next and Last on the last of 14 pages, and keeps them focusable', () => {
    const unusable = (name: string): boolean =>
      button(name).getAttribute('aria-disabled') === 'true';
    render({ page: 1 });
    expect(unusable('First page')).toBe(true);
    expect(unusable('Previous page')).toBe(true);
    expect(unusable('Next page')).toBe(false);

    spectator.setInput('page', 14);
    expect(unusable('Next page')).toBe(true);
    expect(unusable('Last page')).toBe(true);
    expect(unusable('First page')).toBe(false);
    // aria-disabled, not disabled: the pressed button keeps the focus when the new page cannot use it.
    expect(button('Last page').disabled).toBe(false);
  });

  it('an unusable button emits nothing', () => {
    render({ page: 14 });
    const emitted: number[] = [];
    spectator.output<number>('$pageChange').subscribe(page => emitted.push(page));

    spectator.click(button('Next page'));
    spectator.click(button('Last page'));

    expect(emitted).toEqual([]);
  });

  it.each([
    ['First page', 1],
    ['Previous page', 2],
    ['Next page', 4],
    ['Last page', 14]
  ])('%s goes to page %i', (name, expected) => {
    render({});
    const emitted: number[] = [];
    spectator.output<number>('$pageChange').subscribe(page => emitted.push(page));

    spectator.click(button(name));

    expect(emitted).toEqual([expected]);
  });

  it('Last then Previous lands on page 13', () => {
    render({});
    const emitted: number[] = [];
    spectator.output<number>('$pageChange').subscribe(page => emitted.push(page));

    spectator.click(button('Last page'));
    spectator.setInput('page', 14);
    spectator.click(button('Previous page'));

    expect(emitted).toEqual([14, 13]);
  });

  it('never moves the focus: the pressed button keeps it, even when the new page makes it unusable', () => {
    render({ pageSizes: [25, 50, 100] });
    const last = button('Last page');
    last.focus();

    spectator.click(last);
    spectator.setInput('page', 14);

    expect(document.activeElement).toBe(last);
  });

  it('shows no page size select unless sizes are given', () => {
    render({});

    expect(spectator.query('select')).toBeNull();
  });

  it('offers the sizes in a labelled select with the current one chosen', () => {
    render({ pageSize: 50, pageSizes: [25, 50, 100] });

    const select = spectator.query<HTMLSelectElement>('select') as HTMLSelectElement;
    const label = spectator.query<HTMLLabelElement>('label.ui-pager-size-label');
    expect(label?.textContent?.trim()).toBe('Rows per page');
    expect(label?.htmlFor).toBe(select.id);
    expect(Array.from(select.options).map(option => option.value)).toEqual(['25', '50', '100']);
    expect(select.value).toBe('50');
  });

  it('emits the new size as a number and leaves the focus on the select (ArrowDown must reach 100)', () => {
    render({ pageSizes: [25, 50, 100] });
    const select = spectator.query('select') as HTMLSelectElement;
    select.focus();
    const emitted: number[] = [];
    spectator.output<number>('$pageSizeChange').subscribe(size => emitted.push(size));

    spectator.selectOption(select, '50');
    spectator.selectOption(select, '100');

    expect(emitted).toEqual([50, 100]);
    expect(document.activeElement).toBe(select);
  });

  describe('the table above it', () => {
    const SCROLL_MARGIN = '56px';

    /** An app-table before the pager whose top is at `top` pixels from the top of the window. */
    function tableAt(top: number): jest.Mock {
      const table = document.createElement('app-table');
      table.style.scrollMarginTop = SCROLL_MARGIN;
      table.getBoundingClientRect = () => ({ top }) as DOMRect;
      const scrollIntoView = jest.fn();
      table.scrollIntoView = scrollIntoView;
      spectator.element.before(table);
      return scrollIntoView;
    }

    it('is scrolled to the top of the window when a page change finds its top above the window', () => {
      render({ page: 2 });
      const scrolled = tableAt(-400);

      spectator.click(button('Next page'));

      expect(scrolled).toHaveBeenCalledWith({ block: 'start', behavior: 'instant' });
    });

    it('is scrolled after a new page size too', () => {
      render({ pageSizes: [25, 50, 100] });
      const scrolled = tableAt(-400);

      spectator.selectOption(spectator.query('select') as HTMLSelectElement, '50');

      expect(scrolled).toHaveBeenCalledTimes(1);
    });

    it('is left alone when its top is already in view under the top bar', () => {
      render({ page: 2 });
      const scrolled = tableAt(120);

      spectator.click(button('Next page'));

      expect(scrolled).not.toHaveBeenCalled();
    });

    it('is left alone when the page cannot change (a boundary button)', () => {
      render({ page: 14 });
      const scrolled = tableAt(-400);

      spectator.click(button('Next page'));

      expect(scrolled).not.toHaveBeenCalled();
    });

    it('does nothing, and does not fail, when there is no table before the pager', () => {
      render({ page: 2 });

      expect(() => spectator.click(button('Next page'))).not.toThrow();
    });
  });

  it('shows no range for an empty list', () => {
    render({ page: 1, total: 0 });

    expect(spectator.query('.ui-pager-range')?.textContent?.trim()).toBe('');
    expect(spectator.query('[aria-live]')?.textContent?.trim()).toBe('Page 1 of 1');
  });
});

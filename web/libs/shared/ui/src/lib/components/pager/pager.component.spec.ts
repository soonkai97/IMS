import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { PagerFocusTarget, UiPagerComponent } from './pager.component';

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

  it('disables First and Previous on the first page, Next and Last on the last of 14 pages', () => {
    render({ page: 1 });
    expect(button('First page').disabled).toBe(true);
    expect(button('Previous page').disabled).toBe(true);
    expect(button('Next page').disabled).toBe(false);

    spectator.setInput('page', 14);
    expect(button('Next page').disabled).toBe(true);
    expect(button('Last page').disabled).toBe(true);
    expect(button('First page').disabled).toBe(false);
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

  it('moves the focus to the table after a page change, so a button the new page disables never holds it', () => {
    const table: PagerFocusTarget = { focusCaption: jest.fn() };
    render({ table });

    spectator.click(button('Last page'));

    expect(table.focusCaption).toHaveBeenCalledTimes(1);
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

  it('emits the new size as a number and moves the focus to the table', () => {
    const table: PagerFocusTarget = { focusCaption: jest.fn() };
    render({ pageSizes: [25, 50, 100], table });
    const emitted: number[] = [];
    spectator.output<number>('$pageSizeChange').subscribe(size => emitted.push(size));

    spectator.selectOption(spectator.query('select') as HTMLSelectElement, '100');

    expect(emitted).toEqual([100]);
    expect(table.focusCaption).toHaveBeenCalledTimes(1);
  });

  it('shows no range for an empty list', () => {
    render({ page: 1, total: 0 });

    expect(spectator.query('.ui-pager-range')?.textContent?.trim()).toBe('');
    expect(spectator.query('[aria-live]')?.textContent?.trim()).toBe('Page 1 of 1');
  });
});

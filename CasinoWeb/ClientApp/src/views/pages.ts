import { h } from '@/dom';

export interface PageAction {
  /** A link when `href` is set, otherwise a button. */
  href?: string;
  label: string;
  onClick?: () => void;
}

/** A centered card for the login, denied, failed, closed and error screens. */
export function statusPage(title: string, message: string, ...actions: PageAction[]): HTMLElement {
  const buttons = actions.map((action) => {
    if (action.href !== undefined)
      return h('a', { class: 'button', href: action.href }, action.label);
    const button = h('button', { class: 'button', type: 'button' }, action.label);
    if (action.onClick)
      button.addEventListener('click', action.onClick);
    return button;
  });
  return h(
    'main',
    { class: 'page' },
    h('h1', {}, title),
    h('p', {}, message),
    ...(buttons.length > 0 ? [h('div', { class: 'actions' }, ...buttons)] : []),
  );
}

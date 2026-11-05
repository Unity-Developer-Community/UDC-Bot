type Child = Node | string;

/** Builds elements with textContent only, so nothing from the server is ever parsed as HTML. */
export function h<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  attributes: Record<string, string> = {},
  ...children: Child[]
): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(attributes))
    element.setAttribute(key, value);
  element.append(...children);
  return element;
}

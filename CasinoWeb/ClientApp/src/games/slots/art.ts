import type { SlotSymbol } from '@/protocol';

// The art stays in the bot's asset folder, the single copy; Vite bundles what is imported from there.
const images = import.meta.glob<string>('../../../../../DiscordBot/Assets/slots/*.png', { eager: true, import: 'default', query: '?url' });
const licenses = import.meta.glob<string>('../../../../../DiscordBot/Assets/slots/LICENSE-FluentEmoji.txt', { eager: true, import: 'default', query: '?url' });

/** The picture of each symbol except the seven, which the scene draws itself. */
export const SYMBOL_IMAGES: Partial<Record<SlotSymbol, string>> = Object.fromEntries(
  Object.entries(images).map(([path, url]) => [path.split('/').pop()!.replace('.png', ''), url]),
);

/** The MIT license text of the Fluent Emoji art, shipped next to the pictures. */
export const ART_LICENSE_URL: string | undefined = Object.values(licenses)[0];

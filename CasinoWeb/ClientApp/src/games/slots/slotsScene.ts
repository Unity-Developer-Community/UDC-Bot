import Phaser from 'phaser';

import type { SlotsStage } from '@/games/slots/slotsGame';
import type { SlotSymbol } from '@/protocol';

import { SYMBOL_IMAGES } from '@/games/slots/art';
import { landingPosition, visibleRows, wrap } from '@/games/slots/reels';

const WIDTH = 480;
const HEIGHT = 380;
const REEL_COUNT = 3;
const REEL_WIDTH = 120;
const REEL_GAP = 20;
const ROW_HEIGHT = 96;
const WINDOW_TOP = 44;
const WINDOW_HEIGHT = ROW_HEIGHT * 3;
const SYMBOL_SIZE = 80;
const ROWS_ABOVE = 2;
const ROWS_BELOW = 2;

const FIRST_REEL_X = (WIDTH - (REEL_COUNT * REEL_WIDTH + (REEL_COUNT - 1) * REEL_GAP)) / 2 + REEL_WIDTH / 2;
const reelX = (reel: number): number => FIRST_REEL_X + reel * (REEL_WIDTH + REEL_GAP);
const PAYLINE_Y = WINDOW_TOP + ROW_HEIGHT * 1.5;

const BANNERS = { jackpot: 'JACKPOT!', loss: 'No win', win: 'WIN!' } as const;

interface Reel {
  /** Position along the strip, in symbols; the symbol at the rounded position is on the payline. */
  position: number;
  sprites: Phaser.GameObjects.Image[];
  strip: SlotSymbol[];
}

/** The colors come from the page's theme tokens, so the canvas follows the light and dark themes like the rest of the page. */
function token(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

const colorNumber = (css: string): number => Phaser.Display.Color.HexStringToColor(css).color;

function prefersReducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/**
 * The slot machine: three reels behind a window, a payline, and a banner for the result. It draws what it is told and
 * decides nothing; the server's stops say where each reel comes to rest.
 */
export class SlotsScene extends Phaser.Scene implements SlotsStage {
  private banner!: Phaser.GameObjects.Text;
  private heldFrames: Phaser.GameObjects.Rectangle[] = [];
  private heldLabels: Phaser.GameObjects.Text[] = [];
  private paylineGlow!: Phaser.GameObjects.Rectangle;
  private reels: Reel[] = [];
  private readonly strips: SlotSymbol[][];
  private readonly onReelClick: (reel: number) => void;
  private readonly onReady: (stage: SlotsStage) => void;

  constructor(strips: SlotSymbol[][], onReelClick: (reel: number) => void, onReady: (stage: SlotsStage) => void) {
    super('slots');
    this.strips = strips;
    this.onReelClick = onReelClick;
    this.onReady = onReady;
  }

  preload(): void {
    for (const [symbol, url] of Object.entries(SYMBOL_IMAGES))
      this.load.image(symbol, url);
  }

  create(): void {
    this.drawSeven();
    const surface = colorNumber(token('--color-surface'));
    const background = colorNumber(token('--color-bg'));
    const border = colorNumber(token('--color-border'));
    const accent = colorNumber(token('--color-accent'));
    const text = token('--color-text');

    this.add.rectangle(WIDTH / 2, WINDOW_TOP + WINDOW_HEIGHT / 2, WIDTH - 40, WINDOW_HEIGHT, background);

    this.reels = this.strips.map((strip, index) => ({
      // Only the look of the first picture: where a reel rests before the first spin carries no meaning.
      position: Math.floor(Math.random() * strip.length),
      sprites: Array.from({ length: ROWS_ABOVE + 1 + ROWS_BELOW }, () =>
        this.add.image(reelX(index), 0, 'cherry').setDisplaySize(SYMBOL_SIZE, SYMBOL_SIZE)),
      strip,
    }));

    // Symbols above and below the window are drawn too, so these covers hide them.
    this.add.rectangle(WIDTH / 2, WINDOW_TOP / 2, WIDTH, WINDOW_TOP, surface).setDepth(2);
    const belowTop = WINDOW_TOP + WINDOW_HEIGHT;
    this.add.rectangle(WIDTH / 2, belowTop + (HEIGHT - belowTop) / 2, WIDTH, HEIGHT - belowTop, surface).setDepth(2);

    const frame = this.add.graphics().setDepth(3);
    frame.lineStyle(2, border);
    frame.strokeRect(20, WINDOW_TOP, WIDTH - 40, WINDOW_HEIGHT);
    for (let reel = 1; reel < REEL_COUNT; reel++) {
      const x = reelX(reel) - REEL_WIDTH / 2 - REEL_GAP / 2;
      frame.lineBetween(x, WINDOW_TOP, x, WINDOW_TOP + WINDOW_HEIGHT);
    }
    // The payline is a thin line with a marker on each side, so it shows without any color.
    frame.lineStyle(2, accent);
    frame.lineBetween(20, PAYLINE_Y, WIDTH - 20, PAYLINE_Y);
    frame.fillStyle(accent);
    frame.fillTriangle(0, PAYLINE_Y - 10, 18, PAYLINE_Y, 0, PAYLINE_Y + 10);
    frame.fillTriangle(WIDTH, PAYLINE_Y - 10, WIDTH - 18, PAYLINE_Y, WIDTH, PAYLINE_Y + 10);

    this.paylineGlow = this.add.rectangle(WIDTH / 2, PAYLINE_Y, WIDTH - 40, ROW_HEIGHT, accent, 0).setStrokeStyle(6, accent).setDepth(3).setVisible(false);

    this.banner = this.add.text(WIDTH / 2, WINDOW_TOP / 2, '', { color: text, fontFamily: 'system-ui, sans-serif', fontSize: '26px', fontStyle: 'bold' }).setOrigin(0.5).setDepth(4);

    for (let reel = 0; reel < REEL_COUNT; reel++) {
      this.heldFrames.push(this.add.rectangle(reelX(reel), WINDOW_TOP + WINDOW_HEIGHT / 2, REEL_WIDTH, WINDOW_HEIGHT - 6, accent, 0).setStrokeStyle(6, accent).setDepth(3).setVisible(false));
      this.heldLabels.push(this.add.text(reelX(reel), belowTop + 24, 'HELD', { color: text, fontFamily: 'system-ui, sans-serif', fontSize: '22px', fontStyle: 'bold' }).setOrigin(0.5).setDepth(4).setVisible(false));
      this.add.zone(reelX(reel), WINDOW_TOP + WINDOW_HEIGHT / 2, REEL_WIDTH, WINDOW_HEIGHT).setInteractive({ useHandCursor: true }).on('pointerdown', () => this.onReelClick(reel));
    }

    this.layoutReels();
    this.onReady(this);
  }

  override update(): void {
    this.layoutReels();
  }

  clearOutcome(): void {
    this.banner.setText('');
    this.tweens.killTweensOf(this.paylineGlow);
    this.paylineGlow.setVisible(false);
  }

  setHeld(held: readonly boolean[]): void {
    held.forEach((isHeld, reel) => {
      this.heldFrames[reel]?.setVisible(isHeld);
      this.heldLabels[reel]?.setVisible(isHeld);
    });
  }

  showOutcome(outcome: 'jackpot' | 'loss' | 'win'): void {
    this.banner.setText(BANNERS[outcome]);
    if (outcome === 'loss')
      return;
    this.paylineGlow.setVisible(true).setAlpha(1);
    if (!prefersReducedMotion())
      this.tweens.add({ alpha: 0.25, duration: 450, repeat: 3, targets: this.paylineGlow, yoyo: true });
  }

  async spinTo(stops: readonly number[], held: readonly boolean[]): Promise<void> {
    const reduced = prefersReducedMotion();
    const rolling = this.reels.map(async (reel, index) => {
      const stop = stops[index];
      // A reel that is held and already rests on the server's stop stays; one the server rolled again moves even if the player had held it a moment ago.
      if (stop === undefined || (held[index] && wrap(reel.position, reel.strip.length) === stop))
        return;
      // Each reel rolls longer than the one before it, so they stop left to right.
      const travel = reduced ? 0 : 16 + index * 10;
      const target = landingPosition(reel.position, stop, reel.strip.length, travel);
      await new Promise<void>((resolve) => {
        this.tweens.add({
          duration: reduced ? 100 : 900 + index * 450,
          ease: 'Cubic.easeOut',
          onComplete: () => resolve(),
          position: target,
          targets: reel,
        });
      });
      // Back inside the strip, so the number does not grow with every spin; the picture is the same.
      reel.position = wrap(target, reel.strip.length);
    });
    await Promise.all(rolling);
  }

  private drawSeven(): void {
    if (this.textures.exists('seven'))
      return;
    // Not themed: this is the picture of a symbol, like the other pictures, not interface chrome. It matches the Discord casino's red 7.
    const canvas = this.textures.createCanvas('seven', 256, 256)!;
    const context = canvas.getContext();
    context.font = 'bold 230px sans-serif';
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.lineWidth = 12;
    context.strokeStyle = '#ffffff';
    context.fillStyle = '#d62828';
    context.strokeText('7', 128, 140);
    context.fillText('7', 128, 140);
    canvas.refresh();
  }

  private layoutReels(): void {
    for (const reel of this.reels) {
      const rows = visibleRows(reel.strip, reel.position, ROWS_ABOVE, ROWS_BELOW);
      rows.forEach(({ offset, row, symbol }, slot) => {
        reel.sprites[slot]?.setTexture(symbol).setY(PAYLINE_Y + (row - offset) * ROW_HEIGHT);
      });
    }
  }
}

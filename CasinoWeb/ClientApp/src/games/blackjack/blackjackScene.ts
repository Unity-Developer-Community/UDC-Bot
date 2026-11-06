import Phaser from 'phaser';

import type { BlackjackOutcome, BlackjackStage } from '@/games/blackjack/blackjackGame';
import type { Drawn, Step } from '@/games/blackjack/table';
import type { BlackjackHand, Card } from '@/protocol';

import { EMPTY_TABLE, tableSteps } from '@/games/blackjack/table';
import { drawCard, prefersReducedMotion, themeToken } from '@/games/cards/cardDrawing';

const WIDTH = 480;
const CARD_WIDTH = 64;
const CARD_HEIGHT = 90;
const CARD_STEP = 72;
const CARD = { height: CARD_HEIGHT, width: CARD_WIDTH };
const MARGIN = 24;
const DEALER_Y = 110;
const PLAYER_Y = 290;
const SHOE = { x: WIDTH - 44, y: 40 };

const BANNERS: Record<BlackjackOutcome, string> = { blackjack: 'BLACKJACK!', bust: 'BUST', loss: 'DEALER WINS', tie: 'PUSH', win: 'YOU WIN!' };

type Row = 'dealer' | 'player';

/** Where the cards of a row sit, centered, closer together once they would not fit. */
function cardX(index: number, count: number): number {
  const step = count > 1 ? Math.min(CARD_STEP, (WIDTH - 2 * MARGIN - CARD_WIDTH) / (count - 1)) : 0;
  const width = step * (count - 1);
  return WIDTH / 2 - width / 2 + index * step;
}

/**
 * The blackjack table: the dealer's cards on top, the player's below, a value under each row and a banner for the
 * result. It draws what it is told and decides nothing; the server's hand says which cards lie where.
 */
export class BlackjackScene extends Phaser.Scene implements BlackjackStage {
  private banner!: Phaser.GameObjects.Text;
  private cards: Record<Row, Phaser.GameObjects.Container[]> = { dealer: [], player: [] };
  private drawn: Drawn = EMPTY_TABLE;
  private values!: Record<Row, Phaser.GameObjects.Text>;
  private readonly onReady: (stage: BlackjackStage) => void;

  constructor(onReady: (stage: BlackjackStage) => void) {
    super('blackjack');
    this.onReady = onReady;
  }

  create(): void {
    const text = themeToken('--color-text');
    const muted = themeToken('--color-muted');
    const style = { color: muted, fontFamily: 'system-ui, sans-serif', fontSize: '16px' };
    this.add.text(MARGIN, DEALER_Y - CARD_HEIGHT / 2 - 30, 'Dealer', style);
    this.add.text(MARGIN, PLAYER_Y - CARD_HEIGHT / 2 - 30, 'You', style);
    const valueStyle = { ...style, color: text, fontStyle: 'bold' };
    this.values = {
      dealer: this.add.text(WIDTH / 2, DEALER_Y + CARD_HEIGHT / 2 + 14, '', valueStyle).setOrigin(0.5),
      player: this.add.text(WIDTH / 2, PLAYER_Y + CARD_HEIGHT / 2 + 14, '', valueStyle).setOrigin(0.5),
    };
    this.banner = this.add.text(WIDTH / 2, (DEALER_Y + PLAYER_Y) / 2 + 12, '', { color: text, fontFamily: 'system-ui, sans-serif', fontSize: '26px', fontStyle: 'bold' })
      .setOrigin(0.5)
      .setDepth(10);
    this.onReady(this);
  }

  clear(): void {
    for (const card of [...this.cards.dealer, ...this.cards.player]) {
      this.tweens.killTweensOf(card);
      card.destroy();
    }
    this.cards = { dealer: [], player: [] };
    this.drawn = EMPTY_TABLE;
    this.values.dealer.setText('');
    this.values.player.setText('');
  }

  async show(hand: BlackjackHand, animate: boolean): Promise<void> {
    const { drawn, steps } = tableSteps(this.drawn, hand);
    this.drawn = drawn;
    const duration = animate && !prefersReducedMotion() ? 260 : 0;
    for (const step of steps)
      await this.run(step, duration);
    this.values.player.setText(String(hand.playerValue));
    this.values.dealer.setText(hand.finished ? String(hand.dealerValue) : `${hand.dealerValue} + ?`);
  }

  showOutcome(outcome: BlackjackOutcome | null): void {
    this.banner.setText(outcome ? BANNERS[outcome] : '');
  }

  private async move(target: Phaser.GameObjects.GameObject, props: Record<string, number>, duration: number): Promise<void> {
    if (duration === 0) {
      Object.assign(target, props);
      return;
    }
    await new Promise<void>((resolve) => {
      this.tweens.add({ duration, ease: 'Cubic.easeOut', onComplete: () => resolve(), targets: target, ...props });
    });
  }

  private makeCard(card: Card | null): Phaser.GameObjects.Container {
    const container = this.add.container(SHOE.x, SHOE.y);
    container.add(drawCard(this, card, CARD));
    return container;
  }

  /** Slides the cards of a row to their places for its current count. */
  private async layout(row: Row, duration: number): Promise<void> {
    const y = row === 'dealer' ? DEALER_Y : PLAYER_Y;
    const cards = this.cards[row];
    await Promise.all(cards.map(async (card, index) => this.move(card, { x: cardX(index, cards.length), y }, duration)));
  }

  private async run(step: Step, duration: number): Promise<void> {
    if (step.kind === 'reveal') {
      const card = this.cards.dealer[1];
      if (!card)
        return;
      // Turned over like a real card: it narrows to an edge, changes face, and widens again.
      await this.move(card, { scaleX: 0 }, duration / 2);
      card.removeAll(true);
      card.add(drawCard(this, step.card, CARD));
      await this.move(card, { scaleX: 1 }, duration / 2);
      return;
    }
    const row: Row = step.kind === 'player' ? 'player' : 'dealer';
    this.cards[row].push(this.makeCard(step.kind === 'hole' ? null : step.card));
    await this.layout(row, duration);
  }
}
